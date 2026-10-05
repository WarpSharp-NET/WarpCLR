using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed class WarpPortableClosedInitializerSourcePlan
{
    internal const string Semantics = "warp.closed-source-initializers/original-cctor-static-primitive-byte-storage-root-invocation-and-ordered-trigger-no-fault-factory-private-event-admission/0.1";

    private WarpPortableClosedInitializerSourcePlan(WarpPortableMethodGraph graph, WarpPortableTypedProgram program,
        WarpPortableSourceHeapSchema schema, WarpPortableSourceInitializerPlan initialization, ImmutableArray<string> methods)
    {
        Graph = graph; Program = program; Schema = schema; Initialization = initialization; Methods = methods;
        PlanHash = WarpPortableSnapshotIdentity.Hash(new
        {
            Semantics, graph.GraphHash, program.VerifiedHash, schema.SchemaHash, initialization.PlanHash, Methods,
            State = WarpPortableHeapServices.SourceInitializationSemantics,
            StaticViews = WarpPortableSourceMemoryLayout.Semantics,
            Domain = "no-reference-static-storage-no-heap-allocation-no-EH-no-source-implicit-factory;private-source-event-and-Host-admission-required",
            Controller = "no-CAS-token-may-be-held-through-original-guest-cctor-CIL;rooted-initialization-state-is-not-a-controller-grant",
        });
    }

    internal WarpPortableMethodGraph Graph { get; }
    internal WarpPortableTypedProgram Program { get; }
    internal WarpPortableSourceHeapSchema Schema { get; }
    internal WarpPortableSourceInitializerPlan Initialization { get; }
    internal ImmutableArray<string> Methods { get; }
    internal string PlanHash { get; }

    internal static WarpPortableClosedInitializerSourcePlan Capture(WarpPortableMethodGraph graph, WarpPortableTypedProgram program,
        WarpPortableSourceHeapSchema schema)
    {
        WarpPortableSourceInitializerPlan initialization = WarpPortableSourceInitializerPlan.Capture(graph, program, schema);
        if (initialization.Triggers.IsEmpty) { throw Invalid("The source plan requires an exact captured initializer trigger.", 0); }
        var sources = graph.Methods.ToDictionary(method => method.Identity, StringComparer.Ordinal);
        var pending = new Queue<string>(); pending.Enqueue(graph.EntryIdentity);
        var found = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out string? identity))
        {
            if (!found.Add(identity)) { continue; }
            WarpPortableMethodGraphMethod source = sources[identity];
            WarpPortableTypedMethod typed = program.Methods.First(method => string.Equals(method.Identity, identity, StringComparison.Ordinal));
            if (!source.SourceMethod.IsStatic || !source.ExceptionRegions.IsEmpty ||
                typed.ArgumentTypes.Concat(typed.LocalTypes).Append(typed.ReturnType).Any(type => !Primitive(program, type)))
            {
                throw Invalid("The finite source initializer path does not bind instance/reference/EH storage.", 0);
            }
            if (string.Equals(identity, graph.EntryIdentity, StringComparison.Ordinal) && program.EntryInitializerTrigger is { } entry)
            {
                pending.Enqueue(entry.Initializer);
            }
            foreach (WarpPortableTypedInstruction instruction in typed.Instructions.Where(instruction => instruction.Reachable))
            {
                CheckInstruction(graph, program, schema, sources, identity, instruction, pending);
            }
        }
        return new(graph, program, schema, initialization, found.Order(StringComparer.Ordinal).ToImmutableArray());
    }

    internal bool BindsMethod(string identity) => Methods.Contains(identity, StringComparer.Ordinal);
    internal static WarpVerificationException Invalid(string message, int offset) => new("WRPCLR2480", message, offset);
    internal static bool Primitive(WarpPortableTypedProgram program, string identity) => program.Types.First(type =>
        string.Equals(type.Identity, identity, StringComparison.Ordinal)).Category is WarpPortableStackCategory.Void or
            WarpPortableStackCategory.I4 or WarpPortableStackCategory.I8 or WarpPortableStackCategory.Binary32 or WarpPortableStackCategory.Binary64;

    private static void CheckInstruction(WarpPortableMethodGraph graph, WarpPortableTypedProgram program,
        WarpPortableSourceHeapSchema schema, Dictionary<string, WarpPortableMethodGraphMethod> sources, string method,
        WarpPortableTypedInstruction typed, Queue<string> pending)
    {
        WarpPortableMethodGraphInstruction instruction = sources[method].Instructions.First(item => item.Offset == typed.Offset);
        if (typed.EntryStack.Concat(typed.ExitStack).Any(value => !Primitive(program, value.TypeIdentity)) ||
            typed.Faults.Any(fault => fault.Kind is not (WarpPortableTypedFaultKind.TypeInitialization or WarpPortableTypedFaultKind.CalledException)))
        {
            throw Invalid("The source initializer operation requires an unbound owner or operation-specific implicit fault.", typed.Offset);
        }
        if (typed.InitializerTrigger is { } trigger)
        {
            if (typed.Effects.IndexOf(WarpPortableTypedEffect.TypeInitialize) != 0) { throw Invalid("This initializer trigger needs its preceding ordered source effect.", typed.Offset); }
            pending.Enqueue(trigger.Initializer);
        }
        if (instruction.Field is { } field)
        {
            WarpPortableSourceHeapField storage = schema.Fields.First(item => string.Equals(item.Identity, field, StringComparison.Ordinal));
            if (!storage.IsStatic || !Primitive(program, typed.MemoryType!) || instruction.OpCode != OpCodes.Ldsfld && instruction.OpCode != OpCodes.Stsfld ||
                storage.IsReadOnly && instruction.OpCode == OpCodes.Stsfld &&
                    !string.Equals(graph.Types.First(type => string.Equals(type.Identity, sources[method].SourceMethod.DeclaringType is { } declaring ?
                        WarpPortableMethodGraphIdentity.Type(declaring) : string.Empty, StringComparison.Ordinal)).Initializer, method, StringComparison.Ordinal))
            {
                throw Invalid("Only exact primitive static load/store views and declaring-cctor readonly stores are bound.", typed.Offset);
            }
        }
        if (instruction.Method is { } target)
        {
            WarpPortableMethodGraphMethod callee = sources[target];
            if (instruction.OpCode != OpCodes.Call || !callee.SourceMethod.IsStatic) { throw Invalid("The source initializer route does not bind construction or dynamic dispatch.", typed.Offset); }
            if (callee.Intrinsic is null) { pending.Enqueue(target); }
            else if (callee.SourceMethod is not MethodInfo function ||
                !callee.Intrinsic.Contains("numeric.bit-cast.", StringComparison.Ordinal) &&
                    WarpPortableWordMathCatalog.Resolve(function) is not { FaultEntrypoint: null })
            {
                throw Invalid("The source initializer intrinsic lacks its exact fault-free common word binding.", typed.Offset);
            }
        }
        if (instruction.OpCode == OpCodes.Newobj || instruction.OpCode == OpCodes.Throw || instruction.OpCode == OpCodes.Rethrow ||
            instruction.OpCode.Name!.StartsWith("ldind.", StringComparison.Ordinal) || instruction.OpCode.Name.StartsWith("stind.", StringComparison.Ordinal))
        {
            throw Invalid("Allocation, explicit throws and external/interior source memory require their actual additional binding.", typed.Offset);
        }
    }
}
