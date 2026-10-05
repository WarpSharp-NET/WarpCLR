using System.Collections.Immutable;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed record WarpPortableWordBindingCompletion(WarpPortableMethodGraph Graph, WarpPortableTypedProgram Program,
    WarpControlFlowKernel StructuralKernel, ImmutableArray<WarpPortableWordBody> Bodies,
    ImmutableArray<WarpPortableGeneratedServiceImport> Imports, WarpPortableWordEntryProjection EntryProjection,
    string MapsHash)
{
    internal WarpPortableWordBody SourceBody(string methodIdentity) => Bodies.First(body => string.Equals(body.MethodIdentity, methodIdentity, StringComparison.Ordinal));
}
