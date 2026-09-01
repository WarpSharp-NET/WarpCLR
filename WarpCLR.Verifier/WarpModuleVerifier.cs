using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using WarpCLR.IR;

namespace WarpCLR.Verifier;

public sealed class WarpModuleVerifier
{
    private const string ManifestMetadataKey = "WarpCIL.Manifest";

    public WarpVerifiedModule Verify(string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        return Verify(File.ReadAllBytes(assemblyPath));
    }

    public WarpVerifiedModule Verify(ReadOnlyMemory<byte> assemblyBytes)
    {
        if (assemblyBytes.IsEmpty)
        {
            throw new ArgumentException("The assembly cannot be empty.", nameof(assemblyBytes));
        }

        byte[] immutableBytes = assemblyBytes.ToArray();
        try
        {
            using var stream = new MemoryStream(immutableBytes, writable: false);
            using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!peReader.HasMetadata)
            {
                throw Error("WRPCIL2000", "The input does not contain ECMA-335 metadata.");
            }

            MetadataReader metadata = peReader.GetMetadataReader();
            if (!metadata.IsAssembly)
            {
                throw Error("WRPCIL2000", "The input metadata does not define an assembly.");
            }

            string manifestJson = ReadEmbeddedManifest(metadata);
            byte[] manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
            WarpManifestData manifest = WarpManifestParser.Parse(manifestBytes);
            ValidateCapabilities(manifest);

            var entries = new List<WarpVerifiedEntry>(manifest.Entries.Count);
            foreach (WarpManifestEntryData entry in manifest.Entries)
            {
                entries.Add(VerifyEntry(peReader, metadata, entry));
            }

            return new WarpVerifiedModule(
                Convert.ToHexString(SHA256.HashData(manifestBytes)),
                Convert.ToHexString(SHA256.HashData(immutableBytes)),
                manifest.Producer,
                manifest.ProducerVersion,
                entries);
        }
        catch (WarpVerificationException)
        {
            throw;
        }
        catch (BadImageFormatException exception)
        {
            throw Error("WRPCIL2000", $"The input is not a valid ECMA-335 assembly. {exception.Message}");
        }
    }

    private static WarpVerifiedEntry VerifyEntry(
        PEReader peReader,
        MetadataReader metadata,
        WarpManifestEntryData entry)
    {
        MethodDefinitionHandle methodHandle = FindMethod(metadata, entry);
        MethodDefinition method = metadata.GetMethodDefinition(methodHandle);
        MethodSignature<WarpMetadataType> signature = method.DecodeSignature(
            new WarpMetadataTypeProvider(),
            genericContext: null);

        ValidateSignature(method, signature, entry);
        if (method.RelativeVirtualAddress == 0)
        {
            throw EntryError(entry, "The entry point does not have a CIL body.");
        }

        MethodBodyBlock body = peReader.GetMethodBody(method.RelativeVirtualAddress);
        if (body.ExceptionRegions.Length != 0)
        {
            throw EntryError(entry, "Exception regions are outside the integer map profile.");
        }

        int localCount = ValidateLocals(metadata, body, entry);
        byte[] il = body.GetILBytes()
            ?? throw EntryError(entry, "The entry point does not contain CIL bytes.");
        byte[] signatureBytes = metadata.GetBlobBytes(method.Signature);
        byte[] localSignatureBytes = GetLocalSignatureBytes(metadata, body.LocalSignature);
        string identity = $"{entry.Type}.{entry.Method}";
        string actualGraphHash = ComputeGraphHash(identity, signatureBytes, localSignatureBytes, il);
        if (!string.Equals(entry.GraphHash, actualGraphHash, StringComparison.Ordinal))
        {
            throw Error(
                "WRPCIL2004",
                $"Entry point '{identity}' graph hash is '{actualGraphHash}', not '{entry.GraphHash}'.");
        }

        int inputBufferCount = entry.ParameterRoles.Count(role => role == WarpParameterRole.InputBuffer);
        WarpIntegerMapKernel kernel = WarpIntegerMapCilVerifier.Verify(
            new WarpIntegerMapMethodBody(
                identity,
                signature.ParameterTypes.Length,
                inputBufferCount,
                body.MaxStack,
                localCount,
                il));

        return new WarpVerifiedEntry(
            identity,
            actualGraphHash,
            entry.ParameterRoles,
            entry.Capabilities,
            kernel);
    }

    private static void ValidateSignature(
        MethodDefinition method,
        MethodSignature<WarpMetadataType> signature,
        WarpManifestEntryData entry)
    {
        if ((method.Attributes & MethodAttributes.Static) == 0 || signature.Header.IsInstance)
        {
            throw EntryError(entry, "The entry point must be static.");
        }

        if ((method.Attributes & MethodAttributes.Abstract) != 0 || signature.GenericParameterCount != 0)
        {
            throw EntryError(entry, "The entry point must be concrete and nongeneric.");
        }

        if (signature.ReturnType != WarpMetadataType.UInt32)
        {
            throw EntryError(entry, "The entry point return type must be System.UInt32.");
        }

        if (signature.ParameterTypes.Length != entry.ParameterRoles.Count)
        {
            throw EntryError(entry, "The manifest parameter roles do not match the method signature.");
        }

        if (signature.ParameterTypes.Any(type => type != WarpMetadataType.UInt32))
        {
            throw EntryError(entry, "All entry point parameters must have type System.UInt32.");
        }
    }

    private static int ValidateLocals(
        MetadataReader metadata,
        MethodBodyBlock body,
        WarpManifestEntryData entry)
    {
        if (body.LocalSignature.IsNil)
        {
            return 0;
        }

        StandaloneSignature localSignature = metadata.GetStandaloneSignature(body.LocalSignature);
        ImmutableArray<WarpMetadataType> locals = localSignature.DecodeLocalSignature(
            new WarpMetadataTypeProvider(),
            genericContext: null);
        if (locals.Any(type => type != WarpMetadataType.UInt32))
        {
            throw EntryError(entry, "All local variables must have type System.UInt32.");
        }

        return locals.Length;
    }

    private static byte[] GetLocalSignatureBytes(
        MetadataReader metadata,
        StandaloneSignatureHandle handle) => handle.IsNil
            ? []
            : metadata.GetBlobBytes(metadata.GetStandaloneSignature(handle).Signature);

    private static MethodDefinitionHandle FindMethod(
        MetadataReader metadata,
        WarpManifestEntryData entry)
    {
        var candidates = new List<MethodDefinitionHandle>();
        foreach (TypeDefinitionHandle typeHandle in metadata.TypeDefinitions)
        {
            TypeDefinition type = metadata.GetTypeDefinition(typeHandle);
            string typeName = metadata.GetString(type.Name);
            string typeNamespace = metadata.GetString(type.Namespace);
            string identity = string.IsNullOrEmpty(typeNamespace)
                ? typeName
                : $"{typeNamespace}.{typeName}";
            if (!string.Equals(identity, entry.Type, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                MethodDefinition method = metadata.GetMethodDefinition(methodHandle);
                if (string.Equals(metadata.GetString(method.Name), entry.Method, StringComparison.Ordinal))
                {
                    candidates.Add(methodHandle);
                }
            }
        }

        if (candidates.Count != 1)
        {
            throw Error(
                "WRPCIL2003",
                $"Manifest entry '{entry.Type}.{entry.Method}' resolves to {candidates.Count} methods.");
        }

        return candidates[0];
    }

    private static string ReadEmbeddedManifest(MetadataReader metadata)
    {
        string? manifest = null;
        AssemblyDefinition assembly = metadata.GetAssemblyDefinition();
        foreach (CustomAttributeHandle handle in assembly.GetCustomAttributes())
        {
            CustomAttribute attribute = metadata.GetCustomAttribute(handle);
            if (!IsAssemblyMetadataAttribute(metadata, attribute.Constructor))
            {
                continue;
            }

            BlobReader reader = metadata.GetBlobReader(attribute.Value);
            if (reader.ReadUInt16() != 1)
            {
                throw Error("WRPCIL2000", "An assembly metadata attribute has an invalid value.");
            }

            string? key = reader.ReadSerializedString();
            string? value = reader.ReadSerializedString();
            ushort namedArgumentCount = reader.ReadUInt16();
            if (namedArgumentCount != 0 || reader.RemainingBytes != 0)
            {
                throw Error("WRPCIL2000", "An assembly metadata attribute has unexpected data.");
            }

            if (!string.Equals(key, ManifestMetadataKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (manifest is not null)
            {
                throw Error("WRPCIL2000", "The assembly contains more than one WarpCIL manifest.");
            }

            manifest = value
                ?? throw Error("WRPCIL2000", "The WarpCIL manifest value cannot be null.");
        }

        return manifest
            ?? throw Error("WRPCIL2000", "The assembly does not contain a WarpCIL manifest.");
    }

    private static bool IsAssemblyMetadataAttribute(
        MetadataReader metadata,
        EntityHandle constructor)
    {
        if (constructor.Kind != HandleKind.MemberReference)
        {
            return false;
        }

        MemberReference member = metadata.GetMemberReference((MemberReferenceHandle)constructor);
        if (member.Parent.Kind != HandleKind.TypeReference)
        {
            return false;
        }

        TypeReference type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
        return string.Equals(
                   metadata.GetString(type.Namespace),
                   "System.Reflection",
                   StringComparison.Ordinal) &&
               string.Equals(
                   metadata.GetString(type.Name),
                   nameof(AssemblyMetadataAttribute),
                   StringComparison.Ordinal);
    }

    private static void ValidateCapabilities(WarpManifestData manifest)
    {
        foreach (WarpManifestEntryData entry in manifest.Entries)
        {
            if (!entry.Capabilities.SequenceEqual(
                    WarpProfileCatalog.RequiredCapabilities,
                    StringComparer.Ordinal))
            {
                throw Error(
                    "WRPCIL2002",
                    $"Entry point '{entry.Type}.{entry.Method}' does not declare the exact profile capabilities.");
            }
        }
    }

    private static string ComputeGraphHash(
        string identity,
        ReadOnlySpan<byte> signature,
        ReadOnlySpan<byte> localSignature,
        ReadOnlySpan<byte> il)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendField(hash, Encoding.UTF8.GetBytes(identity));
        AppendField(hash, signature);
        AppendField(hash, localSignature);
        AppendField(hash, il);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendField(IncrementalHash hash, ReadOnlySpan<byte> field)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, field.Length);
        hash.AppendData(length);
        hash.AppendData(field);
    }

    private static WarpVerificationException EntryError(
        WarpManifestEntryData entry,
        string message) => Error(
            "WRPCIL1000",
            $"Entry point '{entry.Type}.{entry.Method}' is invalid. {message}");

    private static WarpVerificationException Error(string code, string message) => new(code, message);
}
