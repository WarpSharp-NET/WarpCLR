using System.Collections.Immutable;

namespace WarpCLR.Runtime.Host;

internal sealed record WarpCompiledSourceFault(string PlanIdentity, string ContextIdentity,
    uint Dispatch, uint Worker, uint RunGeneration, uint Kind,
    WarpCompiledSourceLocation Location, ImmutableArray<WarpCompiledSourceLocation> SourceFrames);
