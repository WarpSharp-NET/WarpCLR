namespace WarpCLR.Compiler;

internal sealed record WarpPortableSourceMemoryView(uint OwnerType, uint OwnerKind, uint ByteOffset,
    uint ByteSpan, uint ElementType);
