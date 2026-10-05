namespace WarpCLR.Compiler;

internal sealed record WarpPortableSourceHeapField(string Identity, uint DeclaringType, uint FieldType,
    int SourceByteOffset, int HeapByteOffset, int ByteSize, bool IsStatic, bool IsReadOnly, bool IsLiteral);
