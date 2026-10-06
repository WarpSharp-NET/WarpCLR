using System.Collections.Immutable;

namespace WarpCLR.Compiler;

internal sealed record WarpPortableSourceSegment(int Id, int Function, int Block, int CilOffset, short SourceOpcode,
    int EntryProgramCounter, string MethodIdentity, bool RequiresArena, bool HasSourceFault,
    ImmutableArray<int> LocalProgramCounters, ImmutableArray<int> HelperFunctions,
    ImmutableArray<WarpPortableSourceSegmentFrontier> Frontiers, string RootsHash,
    WarpPortableWordSourceBlock Source, WarpPortableSourceSegmentStorage Storage)
{
    internal int OriginKind { get; } = 1;
    internal string CapturedMethodIdentity { get; init; } = MethodIdentity;
    internal int CapturedMethodIndex { get; init; }
    internal ImmutableArray<WarpPortableSourceGuardedFrontier> GuardedFrontiers { get; init; } = [];
}

