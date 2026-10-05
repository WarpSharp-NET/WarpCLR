using System.Collections.Immutable;

namespace WarpCLR.Compiler;

internal sealed record WarpPortableSourceHeapType(uint Id, string Identity, uint Kind,
    int PayloadBytes, uint PayloadWords, uint ElementType, int ElementByteSize, uint ElementStrideWords,
    int StaticBytes, uint StaticWords, bool ExceptionRecord, bool DelegateRecord, bool TypeRecord, bool BeforeFieldInit, string? Initializer,
    uint ArrayRank, bool VectorArray, uint VectorType,
    ImmutableArray<uint> AssignableTo, ImmutableArray<WarpPortableHeapReferenceLayout> References,
    ImmutableArray<WarpPortableHeapReferenceLayout> StaticReferences);
