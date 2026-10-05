using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableWordLowerer
{
    // The transient registry is private to the actual compiler lowerer. Neither
    // a friend Host nor an arbitrary completion subclass can create an entry.
    // The unexposed final program object is registered and consumed exactly once.
    private static readonly ConditionalWeakTable<WarpPortableWordLoweredProgram, ExceptionAttachmentRegistration> AttachmentRegistrations = new();
    private sealed record ExceptionAttachmentRegistration(WarpPortableMethodGraph Graph,
        WarpPortableSourceHeapSchema Schema, WarpPortableExceptionPlan Plan);

    // Private construction is inside the actual lowerer, after Complete and
    // final kernel creation. Host can inspect only a validated identity's object.
    internal sealed class ExceptionAttachment
    {
        internal const string Semantics = "warp.source-exception-attachment/scoped-once-concrete-plan-exact-final-program-body-site-root-maps-full-ir-raw-utf16-snapshot/0.2";
        private readonly WarpPortableWordLoweredProgram program;

        private ExceptionAttachment(WarpPortableMethodGraph graph, WarpPortableSourceHeapSchema schema,
            WarpPortableWordLoweredProgram program, WarpPortableExceptionPlan plan)
        {
            this.program = program; Plan = plan;
            ValidateProjection(graph, schema, program);
            AttachmentHash = Hash(program);
        }

        internal WarpPortableExceptionPlan Plan { get; }
        internal string AttachmentHash { get; }

        internal static ExceptionAttachment CaptureBoundProgram(WarpPortableWordLoweredProgram program)
        {
            ArgumentNullException.ThrowIfNull(program);
            if (!AttachmentRegistrations.TryGetValue(program, out ExceptionAttachmentRegistration? registration) ||
                !AttachmentRegistrations.Remove(program))
            {
                throw WarpPortableWordProgramIdentity.Invalid("Only an unexposed final program registered by the actual lowerer can capture a concrete EH attachment.");
            }
            return new(registration.Graph, registration.Schema, program, registration.Plan);
        }

        internal void Validate(WarpPortableMethodGraph graph, WarpPortableSourceHeapSchema schema,
            WarpPortableWordLoweredProgram candidate)
        {
            if (!ReferenceEquals(program, candidate))
            {
                throw WarpPortableWordProgramIdentity.Invalid("The concrete EH attachment belongs to another exact compiler-created program object.");
            }
            ValidateProjection(graph, schema, candidate);
            if (!string.Equals(AttachmentHash, Hash(candidate), StringComparison.Ordinal))
            {
                throw WarpPortableWordProgramIdentity.Invalid("The concrete EH attachment differs from its sealed program/plan/source projections.");
            }
        }

        private void ValidateProjection(WarpPortableMethodGraph graph, WarpPortableSourceHeapSchema schema,
            WarpPortableWordLoweredProgram candidate) => RequireExceptionProjection(Plan, graph, candidate.VerifiedProgram, schema,
                candidate.Kernel, candidate.Bodies, candidate.EntryProjection, candidate.MapsHash);

        private string Hash(WarpPortableWordLoweredProgram candidate) => SnapshotHash(new
        {
            Semantics, candidate.GraphHash, candidate.VerifiedHash, Plan.TypeSchemaHash,
            candidate.MapsHash, candidate.LoweredHash, candidate.ExecutionBindingHash, candidate.ExecutionPlanHash,
            ActualExceptionPlanHash = Plan.PlanHash, ExceptionMapsHash = Plan.MapsHash, Plan.TraceProjectionHash,
            SourceProjection = candidate.Bodies, candidate.EntryProjection,
            StructuralLayout = WarpPortableWordProgramIdentity.ComputeLayoutProjection(candidate.Kernel),
            FinalIr = WarpIrHash.Compute(candidate.Kernel),
        });
    }

    private static void RequireExceptionProjection(WarpPortableExceptionPlan plan, WarpPortableMethodGraph graph,
        WarpPortableTypedProgram typed, WarpPortableSourceHeapSchema schema, WarpControlFlowKernel kernel,
        ImmutableArray<WarpPortableWordBody> bodies, WarpPortableWordEntryProjection entry, string mapsHash)
    {
        if (!string.Equals(plan.GraphHash, graph.GraphHash, StringComparison.Ordinal) ||
            !string.Equals(plan.VerifiedHash, typed.VerifiedHash, StringComparison.Ordinal) ||
            !string.Equals(plan.TypeSchemaHash, schema.SchemaHash, StringComparison.Ordinal) ||
            !string.Equals(graph.GraphHash, typed.GraphHash, StringComparison.Ordinal) ||
            !string.Equals(graph.GraphHash, schema.GraphHash, StringComparison.Ordinal) ||
            !string.Equals(typed.VerifiedHash, schema.VerifiedHash, StringComparison.Ordinal) ||
            !string.Equals(WarpPortableWordMapHash.Compute(bodies, entry), mapsHash, StringComparison.Ordinal))
        {
            throw WarpPortableWordProgramIdentity.Invalid("The concrete EH plan has a different captured closure/schema/source map.");
        }
        WarpPortableTypedProgram verified = WarpPortableTypedProgram.Verify(graph, typed.CliSizes);
        if (!string.Equals(verified.VerifiedHash, typed.VerifiedHash, StringComparison.Ordinal) ||
            !string.Equals(SnapshotHash(new { verified.Types, verified.Methods }), SnapshotHash(new { typed.Types, typed.Methods }), StringComparison.Ordinal) ||
            !string.Equals(WarpPortableSourceHeapSchema.Create(graph, verified).SchemaHash, schema.SchemaHash, StringComparison.Ordinal))
        {
            throw WarpPortableWordProgramIdentity.Invalid("The attached EH plan requires exact fresh captured-source verification and schema.");
        }
        // This transient read-only projection is never sealed or exposed for
        // execution. The canonical verifier checks all original/alias storage,
        // initialized states, roots, return tuples and generated source charges.
        ValidateSourceMaps(graph, new(typed, string.Empty, mapsHash, kernel, bodies, [], entry));
        ImmutableArray<WarpPortableExceptionBodyBinding> expected = bodies.Select(body => new WarpPortableExceptionBodyBinding(
            body.MethodIdentity, body.Function, body.PrivateWordCount, body.EvaluationWordOffset,
            body.SourceBlocks.Select((block, index) => new WarpPortableExceptionSiteBinding(block.Instruction.Offset,
                index == 0 ? [.. block.GeneratedBlocks, 0] : block.GeneratedBlocks)).ToImmutableArray())
            { AliasOwnerFunction = body.AliasOwnerFunction, AliasPrefixWords = body.AliasPrefixWords,
                PrivateTemporaries = body.PrivateTemporaries }).OrderBy(body => body.Function).ToImmutableArray();
        if (!string.Equals(SnapshotHash(expected), SnapshotHash(plan.Bodies), StringComparison.Ordinal))
        {
            throw WarpPortableWordProgramIdentity.Invalid("The concrete EH body/site/temporary map differs from the actual lowered source projection.");
        }
        var layout = new WarpLogicalMachineLayout(kernel);
        WarpPortableExceptionPlan captured = WarpPortableExceptionPlan.Create(graph, verified, schema, layout, expected);
        if (!string.Equals(captured.PlanHash, plan.PlanHash, StringComparison.Ordinal) ||
            !string.Equals(captured.MapsHash, plan.MapsHash, StringComparison.Ordinal) ||
            !string.Equals(captured.TraceProjectionHash, plan.TraceProjectionHash, StringComparison.Ordinal) ||
            !string.Equals(captured.LayoutHash, plan.LayoutHash, StringComparison.Ordinal))
        {
            throw WarpPortableWordProgramIdentity.Invalid("The concrete EH plan's table/fault/trace/layout identity differs from the final compiler projection.");
        }
    }

    private static string SnapshotHash<T>(T value) => Convert.ToHexString(SHA256.HashData(WarpPortableSnapshotIdentity.Serialize(value)));
}
