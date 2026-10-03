using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using WarpCLR.IR;

namespace WarpCLR.Verifier;

internal static class WarpMetadataCompilationAdmission
{
    public static MethodBodyBlock ReadMethodBody(PEReader peReader, int address, string identity)
    {
        BlobReader reader = peReader.GetSectionData(address).GetReader();
        byte first = reader.ReadByte();
        if ((first & 3) == 3)
        {
            _ = reader.ReadByte();
            if ((first & 8) != 0)
            {
                throw new WarpVerificationException("WRPCIL1000", $"Method '{identity}' contains sections outside the integer map profile.");
            }

            int maxStack = reader.ReadUInt16();
            int codeSize = reader.ReadInt32();
            WarpCompilationAdmission.Require(identity, WarpCompilationResourceKind.EvaluationStack, maxStack, WarpCompilationAdmission.MaximumEvaluationStackPerBody);
            WarpCompilationAdmission.Require(identity, WarpCompilationResourceKind.CilBytes, codeSize, WarpCompilationAdmission.MaximumCilBytesPerBody);
        }

        return peReader.GetMethodBody(address);
    }

    public static MethodSignature<WarpMetadataType> ReadMethodSignature(MetadataReader metadata, MethodDefinition method, string identity)
    {
        BlobReader reader = metadata.GetBlobReader(method.Signature);
        SignatureHeader header = reader.ReadSignatureHeader();
        if (header.Kind != SignatureKind.Method)
        {
            throw new WarpVerificationException("WRPCIL2000", "The method does not have a method signature.");
        }

        int genericCount = header.IsGeneric ? reader.ReadCompressedInteger() : 0;
        int parameters = reader.ReadCompressedInteger();
        WarpCompilationAdmission.Require(identity, WarpCompilationResourceKind.Parameters, parameters, WarpCompilationAdmission.MaximumParametersPerBody);
        WarpCompilationAdmission.Require(identity, WarpCompilationResourceKind.Parameters, genericCount, WarpCompilationAdmission.MaximumParametersPerBody);
        if (reader.ReadSignatureTypeCode() != SignatureTypeCode.UInt32)
        {
            return new MethodSignature<WarpMetadataType>(header, WarpMetadataType.Unsupported, parameters, genericCount, []);
        }

        for (int index = 0; index < parameters; index++)
        {
            if (reader.ReadSignatureTypeCode() != SignatureTypeCode.UInt32)
            {
                // Unsupported compound types never enter the recursive ECMA signature decoder.
                var unsupported = ImmutableArray.CreateBuilder<WarpMetadataType>(parameters);
                for (int parameter = 0; parameter < parameters; parameter++)
                {
                    unsupported.Add(WarpMetadataType.Unsupported);
                }

                return new MethodSignature<WarpMetadataType>(header, WarpMetadataType.UInt32, parameters, genericCount, unsupported.MoveToImmutable());
            }
        }

        RequireComplete(reader);
        return method.DecodeSignature(new WarpMetadataTypeProvider(), genericContext: null);
    }

    public static ImmutableArray<WarpMetadataType> ReadLocalSignature(MetadataReader metadata, StandaloneSignature signature, string identity)
    {
        BlobReader reader = metadata.GetBlobReader(signature.Signature);
        if (reader.ReadSignatureHeader().Kind != SignatureKind.LocalVariables)
        {
            throw new WarpVerificationException("WRPCIL2000", "The locals do not have a local-variable signature.");
        }

        int count = reader.ReadCompressedInteger();
        WarpCompilationAdmission.Require(identity, WarpCompilationResourceKind.Locals, count, WarpCompilationAdmission.MaximumLocalsPerBody);
        for (int index = 0; index < count; index++)
        {
            if (reader.ReadSignatureTypeCode() is not (SignatureTypeCode.UInt32 or SignatureTypeCode.Boolean))
            {
                return [WarpMetadataType.Unsupported];
            }
        }

        RequireComplete(reader);
        return signature.DecodeLocalSignature(new WarpMetadataTypeProvider(), genericContext: null);
    }

    private static void RequireComplete(BlobReader reader)
    {
        if (reader.RemainingBytes != 0)
        {
            throw new WarpVerificationException("WRPCIL2000", "The primitive signature contains trailing bytes.");
        }
    }
}
