using System.Collections.Immutable;
using System.Security.Cryptography;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableExceptionPlan
{
    private readonly ImmutableArray<uint> metadata;

    private WarpPortableExceptionPlan(WarpPortableMethodGraph graph, WarpPortableTypedProgram typed,
        WarpPortableSourceHeapSchema schema, WarpLogicalMachineLayout layout,
        ImmutableArray<WarpPortableExceptionBodyBinding> bodies, ImmutableArray<uint> words,
        ImmutableArray<WarpPortableExceptionFaultBinding> faults, string mapsHash)
    {
        GraphHash = graph.GraphHash; VerifiedHash = typed.VerifiedHash; TypeSchemaHash = schema.SchemaHash;
        LayoutHash = ComputeLayoutHash(layout); MapsHash = mapsHash; Bodies = bodies; Faults = faults; metadata = words;
        FrameWords = layout.FrameWords; PrivateOffset = layout.PrivateOffset; NodeCount = layout.Nodes.Count;
        ExceptionType = schema.TypeId(WarpPortableMethodGraphIdentity.Type(typeof(Exception)));
        TraceType = schema.TypeId(WarpPortableMethodGraphIdentity.Type(typeof(uint[])));
        StackOverflowType = schema.TypeId(WarpPortableMethodGraphIdentity.Type(typeof(StackOverflowException)));
        TraceProjectionHash = Convert.ToHexString(SHA256.HashData(WarpPortableSnapshotIdentity.Serialize(new
        {
            WarpPortableExceptionTraceLayout.Semantics, MapsHash,
            Methods = graph.Methods.Select(method => new { method.Id, method.Identity }),
            Projection = bodies.Select(body => new { body.Function, body.MethodIdentity, CountsSourceDepth = layout.CountsSourceDepth(body.Function), body.AliasOwnerFunction }),
        })));
        PlanHash = Convert.ToHexString(SHA256.HashData(WarpPortableSnapshotIdentity.Serialize(new
        {
            Semantics = WarpPortableExceptionLayout.Semantics, GraphHash, VerifiedHash, TypeSchemaHash,
            LayoutHash, MapsHash, TraceProjectionHash, Bodies, Faults, Words = words,
            HeapOwnership = WarpPortableHeapLayout.Semantics,
            Records = WarpPortableSourceExceptionLayout.Semantics,
            Machine = WarpLogicalMachineLayout.Version,
            LayoutProjection = "warp.exception.layout/full-common-ir-except-final-entry-label/0.1",
            EscapedTerminal = WarpPortableExceptionTerminalLowerer.Semantics,
            Filters = "captured-prefix-alias-separate-null-initialized-evaluation-tail;tail-byrefs-denied",
            ImplicitFactories = "unavailable-until-exact-operation-data-and-compiled-factory-binding",
        })));
    }

    internal string GraphHash { get; }
    internal string VerifiedHash { get; }
    internal string TypeSchemaHash { get; }
    internal string LayoutHash { get; }
    internal string MapsHash { get; }
    internal string PlanHash { get; }
    internal string TraceProjectionHash { get; }
    internal ImmutableArray<WarpPortableExceptionBodyBinding> Bodies { get; }
    internal ImmutableArray<WarpPortableExceptionFaultBinding> Faults { get; }
    internal int FrameWords { get; }
    internal int PrivateOffset { get; }
    internal int NodeCount { get; }
    internal uint ExceptionType { get; }
    internal uint TraceType { get; }
    internal uint StackOverflowType { get; }

    internal static WarpPortableExceptionPlan Create(WarpPortableMethodGraph graph, WarpPortableTypedProgram typed,
        WarpPortableSourceHeapSchema schema, WarpLogicalMachineLayout layout,
        IEnumerable<WarpPortableExceptionBodyBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(typed);
        ArgumentNullException.ThrowIfNull(schema); ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(bindings);
        if (!string.Equals(graph.GraphHash, typed.GraphHash, StringComparison.Ordinal) ||
            !string.Equals(graph.GraphHash, schema.GraphHash, StringComparison.Ordinal) ||
            !string.Equals(typed.VerifiedHash, schema.VerifiedHash, StringComparison.Ordinal) ||
            !layout.HasFrameOwners || layout.Kernel.Execution is null)
        {
            throw Invalid("The EH table must bind one verified closure, type schema and frame-owned generated machine.");
        }
        ImmutableArray<WarpPortableExceptionBodyBinding> owned = bindings.OrderBy(body => body.Function).ToImmutableArray();
        WarpCompilationAdmission.Require(graph.GraphHash, WarpCompilationResourceKind.Functions, owned.Length,
            WarpCompilationAdmission.MaximumFunctionsPerEntry);
        var builder = new Builder(graph, typed, schema, layout, owned);
        ImmutableArray<uint> words = builder.Build();
        string mapsHash = Convert.ToHexString(SHA256.HashData(WarpPortableSnapshotIdentity.Serialize(new
        {
            Maps = owned, Instructions = typed.Methods.Select(method => new { method.Identity, method.Instructions }),
            MethodIdentities = graph.Methods.Select(method => new { method.Id, method.Identity, method.ExceptionRegions }),
            TraceSemantics = WarpPortableExceptionTraceLayout.Semantics,
            TraceProjection = owned.Select(body => new { body.Function, body.MethodIdentity, CountsSourceDepth = layout.CountsSourceDepth(body.Function), body.AliasOwnerFunction }),
        })));
        return new(graph, typed, schema, layout, owned, words, builder.Faults, mapsHash);
    }

    internal static WarpVerificationException Invalid(string message) => new("WRPCLR2500", message, 0);

    internal static string ComputeLayoutHash(WarpLogicalMachineLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        WarpControlFlowKernel kernel = layout.Kernel;
        // The final entry label may contain PlanHash. Excluding only that label
        // breaks the hash cycle while binding every instruction, destination,
        // function identity and logical/root ownership capability. The Host
        // admission additionally binds the full final WarpIrHash and artifacts.
        var structural = new WarpControlFlowKernel("warp.exception.layout/full-common-ir-except-final-entry-label/0.1",
            kernel.InputBufferCount, kernel.ScalarArgumentCount, kernel.Blocks, kernel.Reduction, kernel.Functions, kernel.Execution);
        return WarpIrHash.Compute(structural);
    }

    private sealed partial class Builder
    {
        private readonly WarpPortableMethodGraph graph;
        private readonly WarpPortableTypedProgram typed;
        private readonly WarpPortableSourceHeapSchema schema;
        private readonly WarpLogicalMachineLayout layout;
        private readonly ImmutableArray<WarpPortableExceptionBodyBinding> bodies;
        private readonly List<uint> words = new(new uint[WarpPortableExceptionLayout.HeaderWords]);
        private readonly Dictionary<(int Function, int Offset), uint> sites = [];
        private readonly Dictionary<(int Function, int Region), uint> clauses = [];
        private readonly Dictionary<(int Function, int Block), int> sourceOffsets = [];
        private readonly Dictionary<(int Function, int Offset), int> sourceEntries = [];
        private readonly List<WarpPortableExceptionFaultBinding> faults = [];

        internal Builder(WarpPortableMethodGraph graph, WarpPortableTypedProgram typed, WarpPortableSourceHeapSchema schema,
            WarpLogicalMachineLayout layout, ImmutableArray<WarpPortableExceptionBodyBinding> bodies)
        {
            this.graph = graph; this.typed = typed; this.schema = schema; this.layout = layout; this.bodies = bodies;
        }

        internal ImmutableArray<WarpPortableExceptionFaultBinding> Faults => faults.ToImmutableArray();

        private uint Next => checked((uint)words.Count);

        private uint Allocate(uint count)
        {
            uint start = Next;
            WarpCompilationAdmission.Require(graph.GraphHash, WarpCompilationResourceKind.VerifierWorkspaceSlots,
                checked((long)start + count), WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry);
            words.AddRange(new uint[count]);
            return start;
        }

        private void Set(uint row, uint field, uint value) => words[checked((int)(row + field))] = value;

        private WarpPortableMethodGraphMethod Method(string identity) => graph.Methods.First(method => string.Equals(method.Identity, identity, StringComparison.Ordinal));

        private WarpPortableTypedMethod Typed(string identity) => typed.Methods.First(method => string.Equals(method.Identity, identity, StringComparison.Ordinal));

        private uint Entry(int function, int offset)
        {
            if (!sourceEntries.TryGetValue((function, offset), out int block)) { throw Invalid("A handler/filter/leave target lacks its exact original generated source entry."); }
            return checked((uint)layout.GetBlockEntry(function, block));
        }
    }
}
