using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
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
        using FileStream stream = File.OpenRead(assemblyPath);
        WarpCompilationAdmission.Require("<module>", WarpCompilationResourceKind.AssemblyBytes, stream.Length, WarpCompilationAdmission.MaximumAssemblyBytes);
        byte[] snapshot = new byte[checked((int)stream.Length)];
        stream.ReadExactly(snapshot);
        if (stream.ReadByte() != -1)
        {
            throw new IOException("The assembly grew during bounded module intake; supply a stable assembly snapshot.");
        }

        return Verify(snapshot);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Preserve the verifier service's existing instance invocation contract used by friend compiler assemblies.")]
    internal IReadOnlyDictionary<string, string> ComputeGraphHashes(
        ReadOnlyMemory<byte> assemblyBytes)
    {
        WarpCompilationAdmission.Require("<module>", WarpCompilationResourceKind.AssemblyBytes, assemblyBytes.Length, WarpCompilationAdmission.MaximumAssemblyBytes);
        if (assemblyBytes.IsEmpty)
        {
            throw new ArgumentException("The assembly cannot be empty.", nameof(assemblyBytes));
        }

        using var stream = new MemoryStream(assemblyBytes.ToArray(), writable: false);
        using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
        if (!peReader.HasMetadata)
        {
            throw Error("WRPCIL2000", "The input does not contain ECMA-335 metadata.");
        }

        MetadataReader metadata = peReader.GetMetadataReader();
        WarpInitializationAdmission.RequireModule(metadata);
        string manifestJson = ReadEmbeddedManifest(metadata);
        WarpManifestData manifest = WarpManifestParser.Parse(Encoding.UTF8.GetBytes(manifestJson));
        ValidateCapabilities(manifest);

        var admission = new WarpModuleCompilationAdmission();
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (WarpManifestEntryData entry in manifest.Entries)
        {
            MethodDefinitionHandle methodHandle = FindMethod(metadata, entry);
            MethodDefinition method = metadata.GetMethodDefinition(methodHandle);
            WarpInitializationAdmission.RequireMethod(metadata, method, $"{entry.Type}.{entry.Method}");
            MethodSignature<WarpMetadataType> signature = WarpMetadataCompilationAdmission.ReadMethodSignature(metadata, method, $"{entry.Type}.{entry.Method}");
            ValidateSignature(method, signature, entry);
            string identity = $"{entry.Type}.{entry.Method}";
            MetadataMethodGraph graph = BuildMethodGraph(
                peReader,
                metadata,
                methodHandle,
                identity,
                admission);
            hashes.Add(identity, ComputeGraphHash(graph.Methods));
        }

        return hashes;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Preserve the maintained public verifier instance API; changing this member to static would break compiled callers.")]
    public WarpVerifiedModule Verify(ReadOnlyMemory<byte> assemblyBytes)
    {
        WarpCompilationAdmission.Require("<module>", WarpCompilationResourceKind.AssemblyBytes, assemblyBytes.Length, WarpCompilationAdmission.MaximumAssemblyBytes);
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
            WarpInitializationAdmission.RequireModule(metadata);
            if (!metadata.IsAssembly)
            {
                throw Error("WRPCIL2000", "The input metadata does not define an assembly.");
            }

            string manifestJson = ReadEmbeddedManifest(metadata);
            byte[] manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
            WarpManifestData manifest = WarpManifestParser.Parse(manifestBytes);
            ValidateCapabilities(manifest);

            var admission = new WarpModuleCompilationAdmission();
            var entries = new List<WarpVerifiedEntry>(manifest.Entries.Count);
            foreach (WarpManifestEntryData entry in manifest.Entries)
            {
                entries.Add(VerifyEntry(peReader, metadata, entry, admission));
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
        WarpManifestEntryData entry,
        WarpModuleCompilationAdmission admission)
    {
        MethodDefinitionHandle methodHandle = FindMethod(metadata, entry);
        MethodDefinition method = metadata.GetMethodDefinition(methodHandle);
        WarpInitializationAdmission.RequireMethod(metadata, method, $"{entry.Type}.{entry.Method}");
        MethodSignature<WarpMetadataType> signature = WarpMetadataCompilationAdmission.ReadMethodSignature(metadata, method, $"{entry.Type}.{entry.Method}");

        ValidateSignature(method, signature, entry);
        string identity = $"{entry.Type}.{entry.Method}";
        MetadataMethodGraph graph = BuildMethodGraph(
            peReader,
            metadata,
            methodHandle,
            identity,
            admission);
        string actualGraphHash = ComputeGraphHash(graph.Methods);
        if (!string.Equals(entry.GraphHash, actualGraphHash, StringComparison.Ordinal))
        {
            throw Error(
                "WRPCIL2004",
                $"Entry point '{identity}' graph hash is '{actualGraphHash}', not '{entry.GraphHash}'.");
        }

        int inputBufferCount = entry.ParameterRoles.Count(role => role == WarpParameterRole.InputBuffer);
        MetadataMethodNode entryMethod = graph.Methods[0];
        WarpIntegerMapKernel kernel = WarpIntegerMapCilVerifier.Verify(
            new WarpIntegerMapMethodBody(
                identity,
                signature.ParameterTypes.Length,
                inputBufferCount,
                entryMethod.MaxStack,
                entryMethod.LocalCount,
                entryMethod.Il,
                entry.Reduction,
                entryMethod.LocalsInitialized,
                entryMethod.CallTargets),
            graph.Methods
                .Skip(1)
                .Select(
                    node => new WarpIntegerMapMethodBody(
                        node.Identity,
                        node.ParameterCount,
                        inputBufferCount: 0,
                        node.MaxStack,
                        node.LocalCount,
                        node.Il,
                        localsInitialized: node.LocalsInitialized,
                        callTargets: node.CallTargets))
                .ToArray(),
            admission);

        admission.AdmitKernel(kernel.ControlFlow);
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
        if (!method.Attributes.HasFlag(MethodAttributes.Static) || signature.Header.IsInstance)
        {
            throw EntryError(entry, "The entry point must be static.");
        }

        if (method.Attributes.HasFlag(MethodAttributes.Abstract) || signature.GenericParameterCount != 0)
        {
            throw EntryError(entry, "The entry point must be concrete and nongeneric.");
        }

        if (signature.Header.CallingConvention != SignatureCallingConvention.Default)
        {
            throw EntryError(entry, "The entry point must use the default managed calling convention.");
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

    private static MetadataMethodGraph BuildMethodGraph(
        PEReader peReader,
        MetadataReader metadata,
        MethodDefinitionHandle entryHandle,
        string entryIdentity,
        WarpModuleCompilationAdmission moduleAdmission)
    {
        var order = new List<MethodDefinitionHandle> { entryHandle };
        var functionIds = new Dictionary<MethodDefinitionHandle, int>();
        var drafts = new Dictionary<MethodDefinitionHandle, MetadataMethodDraft>();
        var visiting = new HashSet<MethodDefinitionHandle>();
        var visited = new HashSet<MethodDefinitionHandle>();
        var admission = new WarpCilCompilationAdmission(entryIdentity, moduleAdmission);

        Visit(entryHandle, entryIdentity, isEntry: true);
        MetadataMethodNode[] methods = order
            .Select(handle => drafts[handle].ToNode())
            .ToArray();
        return new MetadataMethodGraph(methods);

        void Visit(
            MethodDefinitionHandle handle,
            string identity,
            bool isEntry)
        {
            if (visited.Contains(handle))
            {
                return;
            }

            if (!visiting.Add(handle))
            {
                throw Error(
                    "WRPCIL1014",
                    $"Method '{identity}' is in a recursive call graph. " +
                    "Recursion requires the portable logical stack.");
            }

            MetadataMethodDraft draft = CreateMethodDraft(peReader, metadata, handle, identity, isEntry, admission);
            drafts.Add(handle, draft);

            foreach (int token in ReadAdmittedCallTokens(draft, admission))
            {
                MethodDefinitionHandle targetHandle = ResolveCallHandle(token, identity);
                (string targetIdentity, int parameterCount) = ReadClosedCallSignature(metadata, targetHandle);
                RequireAcyclicCall(visiting, targetHandle, identity, targetIdentity);

                if (!functionIds.TryGetValue(targetHandle, out int functionId))
                {
                    functionId = order.Count - 1;
                    functionIds.Add(targetHandle, functionId);
                    order.Add(targetHandle);
                }

                draft.CallTargets.Add(
                    token,
                    new WarpCilCallTarget(
                        functionId,
                        parameterCount,
                        targetIdentity));
                Visit(targetHandle, targetIdentity, isEntry: false);
            }

            visiting.Remove(handle);
            visited.Add(handle);
        }
    }

    private static IEnumerable<int> ReadAdmittedCallTokens(MetadataMethodDraft draft, WarpCilCompilationAdmission admission)
    {
        IReadOnlyList<int> tokens = WarpIntegerMapCilVerifier.ReadCallTokens(draft.Il, draft.Identity, out int instructionCount);
        admission.AdmitDecodedInstructions(instructionCount);
        return tokens.Distinct();
    }

    private static (string Identity, int ParameterCount) ReadClosedCallSignature(
        MetadataReader metadata,
        MethodDefinitionHandle handle)
    {
        MethodDefinition definition = metadata.GetMethodDefinition(handle);
        WarpInitializationAdmission.RequireMethod(metadata, definition, "<closed-method>");
        MethodSignature<WarpMetadataType> signature = WarpMetadataCompilationAdmission.ReadMethodSignature(metadata, definition, "<closed-method>");
        string identity = GetMethodIdentity(metadata, handle, signature.ParameterTypes.Length);
        ValidateClosedFunction(definition, signature, identity);
        return (identity, signature.ParameterTypes.Length);
    }

    private static void RequireAcyclicCall(
        HashSet<MethodDefinitionHandle> visiting,
        MethodDefinitionHandle target,
        string sourceIdentity,
        string targetIdentity)
    {
        if (visiting.Contains(target))
        {
            throw Error(
                "WRPCIL1014",
                $"Call from '{sourceIdentity}' to '{targetIdentity}' is recursive. " +
                "Recursion requires the portable logical stack.");
        }
    }

    private static MetadataMethodDraft CreateMethodDraft(
        PEReader peReader,
        MetadataReader metadata,
        MethodDefinitionHandle handle,
        string identity,
        bool isEntry,
        WarpCilCompilationAdmission admission)
    {
        MethodDefinition definition = metadata.GetMethodDefinition(handle);
        WarpInitializationAdmission.RequireMethod(metadata, definition, identity);
        MethodSignature<WarpMetadataType> signature = WarpMetadataCompilationAdmission.ReadMethodSignature(metadata, definition, identity);
        ValidateDeclaringType(metadata, definition, identity);
        if (!isEntry)
        {
            ValidateClosedFunction(definition, signature, identity);
        }

        if (definition.RelativeVirtualAddress == 0)
        {
            throw MethodError(identity, "The method does not have a CIL body.");
        }

        MethodBodyBlock body = WarpMetadataCompilationAdmission.ReadMethodBody(peReader, definition.RelativeVirtualAddress, identity);
        if (body.ExceptionRegions.Length != 0)
        {
            throw MethodError(identity, "Exception regions are outside the integer map profile.");
        }

        int localCount = ValidateLocals(metadata, body, identity);
        admission.AdmitMethod(identity, signature.ParameterTypes.Length, body.MaxStack, localCount, body.GetILReader().Length, isEntry);
        byte[] il = body.GetILBytes()
            ?? throw MethodError(identity, "The method does not contain CIL bytes.");
        return new MetadataMethodDraft(
            identity,
            signature.ParameterTypes.Length,
            body.MaxStack,
            localCount,
            il,
            body.LocalVariablesInitialized,
            metadata.GetBlobBytes(definition.Signature),
            GetLocalSignatureBytes(metadata, body.LocalSignature));
    }

    private static MethodDefinitionHandle ResolveCallHandle(int token, string identity)
    {
        EntityHandle calledHandle;
        try
        {
            calledHandle = MetadataTokens.EntityHandle(token);
        }
        catch (ArgumentException exception)
        {
            throw Error(
                "WRPCIL1013",
                $"Method '{identity}' contains an invalid call token 0x{token:X8}. " +
                exception.Message);
        }

        if (calledHandle.Kind != HandleKind.MethodDefinition)
        {
            throw Error(
                "WRPCIL1013",
                $"Method '{identity}' calls outside its closed module. " +
                "Only direct MethodDef calls are portable.");
        }

        return (MethodDefinitionHandle)calledHandle;
    }

    private static void ValidateClosedFunction(
        MethodDefinition method,
        MethodSignature<WarpMetadataType> signature,
        string identity)
    {
        if (!method.Attributes.HasFlag(MethodAttributes.Static) || signature.Header.IsInstance)
        {
            throw MethodError(identity, "The method must be static.");
        }

        if (method.Attributes.HasFlag(MethodAttributes.Abstract) || signature.GenericParameterCount != 0)
        {
            throw MethodError(identity, "The method must be concrete and nongeneric.");
        }

        if (signature.Header.CallingConvention != SignatureCallingConvention.Default)
        {
            throw MethodError(identity, "The method must use the default managed calling convention.");
        }

        if (signature.ReturnType != WarpMetadataType.UInt32)
        {
            throw MethodError(identity, "The method return type must be System.UInt32.");
        }

        if (signature.ParameterTypes.Any(type => type != WarpMetadataType.UInt32))
        {
            throw MethodError(identity, "All method parameters must have type System.UInt32.");
        }
    }

    private static void ValidateDeclaringType(
        MetadataReader metadata,
        MethodDefinition method,
        string identity)
    {
        TypeDefinition type = metadata.GetTypeDefinition(method.GetDeclaringType());
        if (!type.GetDeclaringType().IsNil || type.GetGenericParameters().Count != 0)
        {
            throw MethodError(identity, "The declaring type must be top-level and nongeneric.");
        }
    }

    private static string GetMethodIdentity(
        MetadataReader metadata,
        MethodDefinitionHandle handle,
        int parameterCount)
    {
        MethodDefinition method = metadata.GetMethodDefinition(handle);
        TypeDefinition type = metadata.GetTypeDefinition(method.GetDeclaringType());
        string typeName = metadata.GetString(type.Name);
        string typeNamespace = metadata.GetString(type.Namespace);
        string methodName = metadata.GetString(method.Name);
        WarpCompilationAdmission.Require("<metadata-method>", WarpCompilationResourceKind.IdentityCharacters,
            typeName.Length + (long)typeNamespace.Length + methodName.Length + (string.IsNullOrEmpty(typeNamespace) ? 0 : 1) +
            2 + parameterCount.ToString(CultureInfo.InvariantCulture).Length, WarpCompilationAdmission.MaximumIdentityCharacters);
        string declaringType = string.IsNullOrEmpty(typeNamespace)
            ? typeName
            : $"{typeNamespace}.{typeName}";
        return $"{declaringType}.{methodName}/{parameterCount}";
    }

    private static int ValidateLocals(
        MetadataReader metadata,
        MethodBodyBlock body,
        string identity)
    {
        if (body.LocalSignature.IsNil)
        {
            return 0;
        }

        StandaloneSignature localSignature = metadata.GetStandaloneSignature(body.LocalSignature);
        ImmutableArray<WarpMetadataType> locals = WarpMetadataCompilationAdmission.ReadLocalSignature(metadata, localSignature, identity);
        if (locals.Any(
                type => type is not WarpMetadataType.UInt32 and
                    not WarpMetadataType.Boolean))
        {
            throw MethodError(
                identity,
                "All local variables must have type System.UInt32 or System.Boolean.");
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
            WarpCompilationAdmission.Require("<metadata-type>", WarpCompilationResourceKind.IdentityCharacters,
                typeName.Length + (long)typeNamespace.Length + (string.IsNullOrEmpty(typeNamespace) ? 0 : 1), WarpCompilationAdmission.MaximumIdentityCharacters);
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
            WarpCompilationAdmission.Require("<manifest>", WarpCompilationResourceKind.ManifestBytes,
                Encoding.UTF8.GetByteCount(manifest), WarpCompilationAdmission.MaximumManifestBytes);
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

    private static string ComputeGraphHash(IReadOnlyList<MetadataMethodNode> methods)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendField(hash, Encoding.UTF8.GetBytes("warp.method-graph/0.2"));
        Span<byte> count = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(count, methods.Count);
        hash.AppendData(count);
        foreach (MetadataMethodNode method in methods)
        {
            AppendField(hash, Encoding.UTF8.GetBytes(method.Identity));
            AppendInt32(hash, method.MaxStack);
            AppendInt32(hash, method.LocalCount);
            AppendInt32(hash, method.LocalsInitialized ? 1 : 0);
            AppendField(hash, method.Signature);
            AppendField(hash, method.LocalSignature);
            AppendField(hash, method.Il);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendField(IncrementalHash hash, ReadOnlySpan<byte> field)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, field.Length);
        hash.AppendData(length);
        hash.AppendData(field);
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static WarpVerificationException EntryError(
        WarpManifestEntryData entry,
        string message) => Error(
            "WRPCIL1000",
            $"Entry point '{entry.Type}.{entry.Method}' is invalid. {message}");

    private static WarpVerificationException MethodError(
        string identity,
        string message) => Error(
            "WRPCIL1000",
            $"Method '{identity}' is invalid. {message}");

    private static WarpVerificationException Error(string code, string message) => new(code, message);

    private sealed class MetadataMethodDraft
    {
        public MetadataMethodDraft(
            string identity,
            int parameterCount,
            int maxStack,
            int localCount,
            byte[] il,
            bool localsInitialized,
            byte[] signature,
            byte[] localSignature)
        {
            Identity = identity;
            ParameterCount = parameterCount;
            MaxStack = maxStack;
            LocalCount = localCount;
            Il = il;
            LocalsInitialized = localsInitialized;
            Signature = signature;
            LocalSignature = localSignature;
        }

        public string Identity { get; }

        public int ParameterCount { get; }

        public int MaxStack { get; }

        public int LocalCount { get; }

        public byte[] Il { get; }

        public bool LocalsInitialized { get; }

        public byte[] Signature { get; }

        public byte[] LocalSignature { get; }

        public Dictionary<int, WarpCilCallTarget> CallTargets { get; } = [];

        public MetadataMethodNode ToNode() => new(
            Identity,
            ParameterCount,
            MaxStack,
            LocalCount,
            Il,
            LocalsInitialized,
            Signature,
            LocalSignature,
            CallTargets);
    }

    private sealed record MetadataMethodNode(
        string Identity,
        int ParameterCount,
        int MaxStack,
        int LocalCount,
        byte[] Il,
        bool LocalsInitialized,
        byte[] Signature,
        byte[] LocalSignature,
        IReadOnlyDictionary<int, WarpCilCallTarget> CallTargets);

    private sealed record MetadataMethodGraph(IReadOnlyList<MetadataMethodNode> Methods);
}
