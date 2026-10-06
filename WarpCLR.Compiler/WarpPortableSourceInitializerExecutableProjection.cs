using System.Collections.Immutable;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableSourceInitializerExecutableProjection
{
    internal const string Semantics = "warp.source-initializer-executable-projection/final-compiler-object-final-function-node-pc-original-entry-charge-zero-synthetic-invocation-tagged-origin-bounded-helper-guest-frontiers-candidate-only/0.1";
    private readonly WarpPortableMethodGraph graph;
    private readonly WarpPortableSourceHeapSchema schema;
    private readonly WarpPortableWordLoweredProgram program;

    private WarpPortableSourceInitializerExecutableProjection(WarpPortableMethodGraph graph,
        WarpPortableSourceHeapSchema schema, WarpPortableWordLoweredProgram program,
        WarpPortableWordProgramIdentity identity, WarpPortableSourceInitializerPlan initializers,
        WarpLogicalMachineLayout layout, ImmutableArray<WarpPortableSourceInitializerExecutablePoint> points,
        ImmutableArray<WarpPortableSourceInitializerExecutableStep> steps,
        ImmutableArray<WarpPortableSourceInitializerExecutableSite> sites)
    {
        this.graph = graph; this.schema = schema; this.program = program;
        ProgramIdentity = identity; Initializers = initializers; Layout = layout;
        Points = points; SourceSteps = steps; Sites = sites;
        ProjectionHash = WarpPortableSnapshotIdentity.Hash(new
        {
            Semantics, identity.IdentityHash, identity.KernelIrHash, identity.StructuralLayoutHash, identity.MapsHash,
            initializers.PlanHash, Points, SourceSteps, Sites,
            OriginVersion = WarpPortableSourceOperationOrigin.Semantics,
            InvocationVersion = WarpPortableWordInvocationPrelude.Semantics,
            Accounting = "one-source-charge-on-original-block-entry-only;continuations-and-synthetic-nodes-zero;alias-is-original-filter-CIL-with-separate-frame",
            Bounds = WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry,
        });
    }

    internal string ProjectionHash { get; }
    internal WarpPortableWordProgramIdentity ProgramIdentity { get; }
    internal WarpPortableSourceInitializerPlan Initializers { get; }
    internal WarpLogicalMachineLayout Layout { get; }
    internal ImmutableArray<WarpPortableSourceInitializerExecutablePoint> Points { get; }
    internal ImmutableArray<WarpPortableSourceInitializerExecutableStep> SourceSteps { get; }
    internal ImmutableArray<WarpPortableSourceInitializerExecutableSite> Sites { get; }

    internal WarpPortableSourceInitializerCandidate Describe(WarpPortableSourceInitializerExecutableSite site)
    {
        ArgumentNullException.ThrowIfNull(site);
        if (!Sites.Any(item => ReferenceEquals(item, site))) { throw Invalid("The initializer site belongs to another projection."); }
        return Walk(site.EntryProgramCounter);
    }

    internal WarpPortableSourceInitializerCandidate Describe(WarpPortableSourceInitializerExecutableStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (!SourceSteps.Any(item => ReferenceEquals(item, step))) { throw Invalid("The source step belongs to another projection."); }
        return Walk(step.EntryProgramCounter);
    }

    internal void Validate(WarpPortableMethodGraph capturedGraph, WarpPortableSourceHeapSchema capturedSchema,
        WarpPortableWordLoweredProgram finalProgram)
    {
        if (!ReferenceEquals(graph, capturedGraph) || !ReferenceEquals(schema, capturedSchema) || !ReferenceEquals(program, finalProgram))
        {
            throw Invalid("An executable projection binds the exact compiler result object and its captured closure, not a record clone or supplied hash.");
        }
        if (!ReferenceEquals(ProgramIdentity, WarpPortableWordProgramIdentity.Validate(capturedGraph, capturedSchema, finalProgram)))
        {
            throw Invalid("The final compiler identity was replaced.");
        }
    }

    private static WarpVerificationException Invalid(string message, int offset = 0) => new("WRPCLR2500", message, offset);
}
