using System.Collections.Immutable;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed record WarpPortableWordLoweredProgram(WarpPortableTypedProgram VerifiedProgram,
    string LoweredHash, string MapsHash, WarpControlFlowKernel Kernel, ImmutableArray<WarpPortableWordBody> Bodies,
    ImmutableArray<string> RequiredServices, WarpPortableWordEntryProjection EntryProjection)
{
    public string GraphHash => VerifiedProgram.GraphHash;
    public string VerifiedHash => VerifiedProgram.VerifiedHash;
    internal string? ExecutionBindingHash { get; init; }
    internal string? ExecutionPlanHash { get; init; }
    internal WarpPortableWordProgramIdentity? CompilerIdentity { get; private set; }

    internal WarpPortableWordLoweredProgram SealCompilerIdentity(WarpPortableMethodGraph graph, WarpPortableSourceHeapSchema schema)
    {
        if (CompilerIdentity is not null) { throw new InvalidOperationException("A compiler program identity is sealed exactly once."); }
        CompilerIdentity = WarpPortableWordProgramIdentity.Capture(graph, schema, this);
        return this;
    }
}
