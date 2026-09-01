using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace WarpCLR.Verifier;

internal enum WarpMetadataType
{
    Unsupported,
    UInt32,
}

internal sealed class WarpMetadataTypeProvider : ISignatureTypeProvider<WarpMetadataType, object?>
{
    public WarpMetadataType GetArrayType(WarpMetadataType elementType, ArrayShape shape) =>
        WarpMetadataType.Unsupported;

    public WarpMetadataType GetByReferenceType(WarpMetadataType elementType) =>
        WarpMetadataType.Unsupported;

    public WarpMetadataType GetFunctionPointerType(MethodSignature<WarpMetadataType> signature) =>
        WarpMetadataType.Unsupported;

    public WarpMetadataType GetGenericInstantiation(
        WarpMetadataType genericType,
        ImmutableArray<WarpMetadataType> typeArguments) => WarpMetadataType.Unsupported;

    public WarpMetadataType GetGenericMethodParameter(object? genericContext, int index) =>
        WarpMetadataType.Unsupported;

    public WarpMetadataType GetGenericTypeParameter(object? genericContext, int index) =>
        WarpMetadataType.Unsupported;

    public WarpMetadataType GetModifiedType(
        WarpMetadataType modifier,
        WarpMetadataType unmodifiedType,
        bool isRequired) => WarpMetadataType.Unsupported;

    public WarpMetadataType GetPinnedType(WarpMetadataType elementType) =>
        WarpMetadataType.Unsupported;

    public WarpMetadataType GetPointerType(WarpMetadataType elementType) =>
        WarpMetadataType.Unsupported;

    public WarpMetadataType GetPrimitiveType(PrimitiveTypeCode typeCode) =>
        typeCode == PrimitiveTypeCode.UInt32
            ? WarpMetadataType.UInt32
            : WarpMetadataType.Unsupported;

    public WarpMetadataType GetSZArrayType(WarpMetadataType elementType) =>
        WarpMetadataType.Unsupported;

    public WarpMetadataType GetTypeFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        byte rawTypeKind) => WarpMetadataType.Unsupported;

    public WarpMetadataType GetTypeFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        byte rawTypeKind) => WarpMetadataType.Unsupported;

    public WarpMetadataType GetTypeFromSpecification(
        MetadataReader reader,
        object? genericContext,
        TypeSpecificationHandle handle,
        byte rawTypeKind) => WarpMetadataType.Unsupported;
}
