namespace WarpCLR.Compiler;

internal sealed record WarpPortableSourceNullableLayout(uint Type, uint ElementType, uint HasValueByteOffset,
    uint ValueByteOffset);
