using System.Collections.Immutable;

namespace WarpCLR.Verifier;

internal sealed record WarpPortableTypedType(
    string Identity, WarpPortableStackCategory Category, int StorageBits,
    bool IsSigned, int ByteSize, int Alignment, int WordCount, int InstanceByteSize,
    string? ElementType, ImmutableArray<WarpPortableTypedField> Fields,
    ImmutableArray<int> ManagedRootByteOffsets);
