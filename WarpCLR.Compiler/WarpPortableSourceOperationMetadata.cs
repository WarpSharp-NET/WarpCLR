using System.Collections.Immutable;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed record WarpPortableSourceOperationMetadata(bool WritesOwnerReferences, bool ReadsOwnerReferences, bool RequiresLease,
    bool MayTargetCallerFrame, ImmutableArray<int> MemoryOwnerByteOffsets,
    ImmutableArray<WarpPortableTypedProvenance> DestinationProvenance)
{
    internal const string Version = "warp.source-effects/typed-owner-write-lease-source-completion/0.1";
}
