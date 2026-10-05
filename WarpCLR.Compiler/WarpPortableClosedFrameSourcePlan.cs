using System.Collections.Immutable;
using System.Reflection.Emit;
using System.Security.Cryptography;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

// A closed source domain, not a grant to invoke a program with caller-supplied byrefs.
// Every admitted byref is made inside a live source frame. Heap/static/alias owners
// and source implicit exceptions require their separate runtime admission.
internal sealed partial class WarpPortableClosedFrameSourcePlan
{
    internal const string Semantics = "warp.source-closed-frame/non-null-internal-owner-provenance-value-constructors-exact-byte-views-concrete-eh-ownership-raw-utf16-snapshot/0.3";
    private readonly Dictionary<string, WarpPortableMethodGraphMethod> methods;
    private readonly Dictionary<string, WarpPortableTypedType> types;
    private readonly WarpPortableExceptionSourceBinding? exceptions;

    private WarpPortableClosedFrameSourcePlan(WarpPortableMethodGraph graph, WarpPortableTypedProgram program,
        WarpPortableSourceHeapSchema schema, WarpPortableExceptionSourceBinding? exceptions = null)
    {
        Graph = graph; Program = program; Schema = schema;
        this.exceptions = exceptions;
        methods = graph.Methods.ToDictionary(method => method.Identity, StringComparer.Ordinal);
        types = program.Types.ToDictionary(type => type.Identity, StringComparer.Ordinal);
        Methods = Closure();
        CheckEntry();
        foreach (string identity in Methods) { CheckMethod(methods[identity]); }
        PlanHash = Convert.ToHexString(SHA256.HashData(WarpPortableSnapshotIdentity.Serialize(new
        {
            Semantics, graph.GraphHash, program.VerifiedHash, schema.SchemaHash, Methods,
            Native = WarpPortableCliNativeInteger.Semantics, Cli = program.CliSizes?.ContractHash,
            Frames = WarpPortableSourceFrameSchema.Semantics, Storage = WarpPortableWordPrivateTemporary.Semantics,
            Machine = WarpLogicalMachineLayout.Version,
            ExceptionBinding = exceptions?.BindingHash, ExceptionOwnership = exceptions?.InstructionOwnershipHash,
        })));
    }

    internal WarpPortableMethodGraph Graph { get; }
    internal WarpPortableTypedProgram Program { get; }
    internal WarpPortableSourceHeapSchema Schema { get; }
    internal ImmutableArray<string> Methods { get; }
    internal string PlanHash { get; }
    internal string? ExceptionOwnershipHash => exceptions?.InstructionOwnershipHash;

    internal static WarpPortableClosedFrameSourcePlan Capture(WarpPortableMethodGraph graph,
        WarpPortableTypedProgram program, WarpPortableSourceHeapSchema schema)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(program); ArgumentNullException.ThrowIfNull(schema);
        if (!string.Equals(graph.GraphHash, program.GraphHash, StringComparison.Ordinal) ||
            !string.Equals(schema.GraphHash, graph.GraphHash, StringComparison.Ordinal) ||
            !string.Equals(schema.VerifiedHash, program.VerifiedHash, StringComparison.Ordinal))
        {
            throw Invalid("The closed frame source domain requires its exact graph, typed program and schema.", 0);
        }
        return new(graph, program, schema);
    }

    internal static WarpPortableClosedFrameSourcePlan CaptureForExceptionComposition(WarpPortableMethodGraph graph,
        WarpPortableTypedProgram program, WarpPortableSourceHeapSchema schema, WarpPortableExceptionSourceBinding exceptions)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(program); ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(exceptions);
        exceptions.RequireClosure(graph, program, schema);
        return new(graph, program, schema, exceptions);
    }

    private ImmutableArray<string> Closure()
    {
        var pending = new Queue<string>(); pending.Enqueue(Graph.EntryIdentity);
        if (exceptions is not null)
        {
            foreach (string identity in exceptions.AdditionalSourceMethods.Concat(exceptions.ExceptionMethods)) { pending.Enqueue(identity); }
        }
        var closed = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out string? identity))
        {
            if (!closed.Add(identity)) { continue; }
            WarpPortableMethodGraphMethod method = methods[identity];
            if (method.Intrinsic is not null || method.Instructions.IsEmpty) { throw Invalid("A closed source function needs captured CIL.", 0); }
            WarpCompilationAdmission.Require(identity, WarpCompilationResourceKind.Functions, closed.Count,
                WarpCompilationAdmission.MaximumFunctionsPerEntry);
            WarpPortableTypedMethod typed = Program.Methods.First(item => string.Equals(item.Identity, identity, StringComparison.Ordinal));
            foreach (WarpPortableTypedInstruction instruction in typed.Instructions.Where(item => item.Reachable))
            {
                WarpPortableMethodGraphInstruction input = method.Instructions.First(item => item.Offset == instruction.Offset);
                if (input.OpCode != OpCodes.Call && input.OpCode != OpCodes.Newobj) { continue; }
                WarpPortableMethodGraphMethod target = methods[input.Method!];
                if (target.Intrinsic is null) { pending.Enqueue(target.Identity); }
            }
        }
        return closed.Order(StringComparer.Ordinal).ToImmutableArray();
    }

    private void CheckEntry()
    {
        WarpPortableTypedMethod entry = Program.Methods.First(method => string.Equals(method.Identity, Graph.EntryIdentity, StringComparison.Ordinal));
        if (!methods[entry.Identity].SourceMethod.IsStatic || entry.ArgumentTypes.Any(ContainsBorrowedStorage))
        {
            throw Invalid("The frame-only source domain cannot accept an external managed-byref receiver/argument.", 0);
        }
    }

    private void CheckMethod(WarpPortableMethodGraphMethod method)
    {
        WarpPortableTypedMethod typed = Program.Methods.First(item => string.Equals(item.Identity, method.Identity, StringComparison.Ordinal));
        WarpPortableMethodGraphType declaring = Graph.Types.First(type => type.SourceType == method.SourceMethod.DeclaringType);
        if (!method.ExceptionRegions.IsEmpty && exceptions is null || declaring.Initializer is not null ||
            ContainsBorrowedStorage(typed.ReturnType) ||
            !method.SourceMethod.IsStatic && !method.SourceMethod.DeclaringType!.IsValueType)
        {
            throw Invalid("Source EH, initializers, byref returns and heap receivers require their separate complete binding.", 0);
        }
        foreach (WarpPortableTypedInstruction instruction in typed.Instructions.Where(item => item.Reachable))
        {
            WarpPortableMethodGraphInstruction original = method.Instructions.First(item => item.Offset == instruction.Offset);
            CheckInstruction(method.Identity, original, instruction);
        }
    }

    private bool ContainsBorrowedStorage(string identity)
    {
        var pending = new Queue<string>(); pending.Enqueue(identity);
        var examined = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out string? current))
        {
            if (!examined.Add(current)) { continue; }
            WarpPortableTypedType type = types[current];
            if (type.Category is WarpPortableStackCategory.ManagedByref or WarpPortableStackCategory.FunctionTarget) { return true; }
            if (type.Category != WarpPortableStackCategory.Value) { continue; }
            foreach (WarpPortableTypedField field in type.Fields.Where(field => !field.IsStatic)) { pending.Enqueue(field.TypeIdentity); }
            WarpCompilationAdmission.Require(identity, WarpCompilationResourceKind.VerifierWorkspaceSlots,
                examined.Count + (long)pending.Count, WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry);
        }
        return false;
    }

    internal static WarpVerificationException Invalid(string message, int offset) => new("WRPCLR2430", message, offset);
}
