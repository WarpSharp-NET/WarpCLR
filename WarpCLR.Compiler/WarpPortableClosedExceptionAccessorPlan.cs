using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed class WarpPortableClosedExceptionAccessorPlan
{
    internal const string Semantics = "warp.closed-exception-accessor-source/captured-original-cil-typed-roots-initialized-nonnull-input-domain-no-implicit-factory-grant/0.1";

    private WarpPortableClosedExceptionAccessorPlan(WarpPortableMethodGraph graph, WarpPortableTypedProgram program,
        WarpPortableSourceHeapSchema schema, ImmutableArray<string> methods, ImmutableArray<WarpPortableClosedExceptionAccessor> accessors)
    {
        Graph = graph; Program = program; Schema = schema; Methods = methods; Accessors = accessors;
        PlanHash = WarpPortableSnapshotIdentity.Hash(new
        {
            Semantics, graph.GraphHash, program.VerifiedHash, schema.SchemaHash, Methods, Accessors,
            InvocationDomain = "private-admission-required-nonnull-exact-assignable-exception-generation-data-initialized-held-operation-lease-precise-input-owner-roots",
            Data = WarpPortableHeapServices.ExceptionAccessorSemantics,
            Catalog = WarpPortableMethodGraphIntrinsics.ExceptionAccessorContract,
            Dispatch = "callvirt-validates-actual-dense-type-against-exact-captured-base-data-targets-including-noninstantiated-field-types;user-overrides-never-substitute-base-fields",
        });
    }

    internal WarpPortableMethodGraph Graph { get; }
    internal WarpPortableTypedProgram Program { get; }
    internal WarpPortableSourceHeapSchema Schema { get; }
    internal string PlanHash { get; }
    internal ImmutableArray<string> Methods { get; }
    internal ImmutableArray<WarpPortableClosedExceptionAccessor> Accessors { get; }

    internal static WarpPortableClosedExceptionAccessorPlan Capture(WarpPortableMethodGraph graph, WarpPortableTypedProgram program,
        WarpPortableSourceHeapSchema schema)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(program); ArgumentNullException.ThrowIfNull(schema);
        if (!string.Equals(graph.GraphHash, program.GraphHash, StringComparison.Ordinal) ||
            !string.Equals(graph.GraphHash, schema.GraphHash, StringComparison.Ordinal) ||
            !string.Equals(program.VerifiedHash, schema.VerifiedHash, StringComparison.Ordinal)) { throw Invalid("The accessor source requires its exact captured closure, typed maps and schema.", 0); }
        if (program.EntryInitializerTrigger is not null) { throw Invalid("An accessor input-domain plan does not bind source type initialization.", 0); }
        var rows = ImmutableArray.CreateBuilder<WarpPortableClosedExceptionAccessor>();
        var sources = graph.Methods.ToDictionary(method => method.Identity, StringComparer.Ordinal);
        ImmutableArray<string> methods = graph.Methods.Where(method => method.Intrinsic is null && !method.Instructions.IsEmpty)
            .Select(method => method.Identity).Order(StringComparer.Ordinal).ToImmutableArray();
        foreach (string identity in methods)
        {
            WarpPortableMethodGraphMethod source = sources[identity];
            if (!source.SourceMethod.IsStatic || !source.ExceptionRegions.IsEmpty) { throw Invalid("The closed accessor source does not bind instance calls or source EH.", 0); }
            foreach (WarpPortableTypedInstruction typed in program.Methods.First(method => string.Equals(method.Identity, identity, StringComparison.Ordinal)).Instructions.Where(instruction => instruction.Reachable))
            {
                WarpPortableMethodGraphInstruction instruction = source.Instructions.First(item => item.Offset == typed.Offset);
                if (typed.InitializerTrigger is not null) { throw Invalid("The accessor source requires a distinct compiled initializer binding.", typed.Offset); }
                if (instruction.Method is { } targetIdentity && instruction.OpCode is var operation && (operation == OpCodes.Call || operation == OpCodes.Callvirt))
                {
                    WarpPortableMethodGraphMethod target = sources[targetIdentity];
                    WarpPortableExceptionAccessorKind? accessor = WarpPortableMethodGraphIntrinsics.ExceptionAccessor(target.SourceMethod);
                    if (accessor is { } kind && kind is not (WarpPortableExceptionAccessorKind.Message or WarpPortableExceptionAccessorKind.StackTrace))
                    {
                        int receiver = typed.EntryStack.Length - (kind == WarpPortableExceptionAccessorKind.HResultStore ? 2 : 1);
                        if (receiver < 0 || typed.EntryStack[receiver].IsNull) { throw Invalid("An explicit null accessor cannot enter the nonnull prepared-owner domain.", typed.Offset); }
                        if (graph.Dispatches.Any(dispatch => string.Equals(dispatch.Slot, targetIdentity, StringComparison.Ordinal) &&
                            !string.Equals(dispatch.Target, targetIdentity, StringComparison.Ordinal))) { throw Invalid("A virtual accessor has a distinct captured override and cannot substitute base data.", typed.Offset); }
                        rows.Add(new(identity, typed.Offset, unchecked((ushort)typed.OpCode), targetIdentity,
                            schema.TypeId(WarpPortableMethodGraphIdentity.Type(target.SourceMethod.DeclaringType!)), kind, WarpPortableSnapshotIdentity.Hash(typed),
                            operation == OpCodes.Callvirt && target.SourceMethod is MethodInfo { IsVirtual: true }, ExactDataTargets(schema, target.SourceMethod)));
                        continue;
                    }
                    if (target.Intrinsic is null && target.SourceMethod.IsStatic && operation == OpCodes.Call) { continue; }
                    if (target.Intrinsic?.Contains("object.reference-equality", StringComparison.Ordinal) == true) { continue; }
                    throw Invalid("This source call lacks an exact compiled data accessor or captured static source target.", typed.Offset);
                }
                if (!typed.Faults.IsEmpty || instruction.OpCode == OpCodes.Newobj || instruction.OpCode == OpCodes.Ldstr ||
                    typed.EntryStack.Any(value => value.Category == WarpPortableStackCategory.ManagedByref))
                {
                    throw Invalid("This source operation requires another actual heap/fault/byref execution binding.", typed.Offset);
                }
            }
        }
        if (rows.Count == 0) { throw Invalid("A closed accessor source must contain an exact captured accessor.", 0); }
        return new(graph, program, schema, methods, rows.ToImmutable());
    }

    internal static WarpVerificationException Invalid(string message, int offset) => new("WRPCLR2470", message, offset);

    private static ImmutableArray<uint> ExactDataTargets(WarpPortableSourceHeapSchema schema, MethodBase target)
    {
        if (target is not MethodInfo { IsVirtual: true } slot) { return []; }
        MethodInfo definition = slot.GetBaseDefinition();
        var admitted = ImmutableArray.CreateBuilder<uint>();
        foreach (WarpPortableSourceHeapType type in schema.Types)
        {
            Type actual = schema.SourceTypes[type.Identity];
            if (!typeof(Exception).IsAssignableFrom(actual) || !slot.DeclaringType!.IsAssignableFrom(actual)) { continue; }
            MethodInfo? selected = null;
            for (Type? current = actual; current is not null && selected is null; current = current.BaseType)
            {
                selected = current.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(method => method.IsVirtual && WarpPortableMethodGraphIntrinsics.SameExceptionMethod(method.GetBaseDefinition(), definition));
            }
            if (selected is not null && WarpPortableMethodGraphIntrinsics.SameExceptionMethod(selected, slot)) { admitted.Add(type.Id); }
        }
        return admitted.ToImmutable();
    }
}
