using System.Collections.Immutable;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

// Only the actual lowerer creates the scoped concrete completion capability.
// Stored contexts close after Complete; immutable snapshots may be copied while
// it is open, but an arbitrary matching hash cannot attach an EH plan.
internal abstract class WarpPortableWordBindingCompletion
{
    internal abstract WarpPortableMethodGraph Graph { get; }
    internal abstract WarpPortableTypedProgram Program { get; }
    internal abstract WarpControlFlowKernel StructuralKernel { get; }
    internal abstract ImmutableArray<WarpPortableWordBody> Bodies { get; }
    internal abstract ImmutableArray<WarpPortableGeneratedServiceImport> Imports { get; }
    internal abstract WarpPortableWordEntryProjection EntryProjection { get; }
    internal abstract string MapsHash { get; }
    internal abstract void AttachExceptionPlan(WarpPortableExceptionPlan plan);

    internal WarpPortableWordBody SourceBody(string methodIdentity) => Bodies.First(body => string.Equals(body.MethodIdentity, methodIdentity, StringComparison.Ordinal));
}
