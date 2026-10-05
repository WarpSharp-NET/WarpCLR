using System.Collections.Immutable;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed class WarpPortableSourceInitializerPlan
{
    internal const string Semantics = "warp.source-initializers/exact-source-trigger-effects-context-cached-wrapper-original-inner-unwrapped-resource-termination/0.1";

    private WarpPortableSourceInitializerPlan(WarpPortableMethodGraph graph, WarpPortableTypedProgram program,
        WarpPortableSourceHeapSchema schema, ImmutableArray<WarpPortableSourceInitializerType> types,
        ImmutableArray<WarpPortableSourceInitializerTrigger> triggers)
    {
        GraphHash = graph.GraphHash; VerifiedHash = program.VerifiedHash; TypeSchemaHash = schema.SchemaHash;
        Types = types; Triggers = triggers;
        PlanHash = WarpPortableSnapshotIdentity.Hash(new
        {
            Semantics, TriggerSemantics = WarpPortableTypeInitialization.Semantics, GraphHash, VerifiedHash, TypeSchemaHash, Types, Triggers,
            TypeNameSemantics = WarpPortableTypeInitializerName.Semantics,
            Prefix = WarpPortableSourceExceptionLayout.Semantics,
            Message = WarpPortableSourceExceptionLayout.MessageWord, Inner = WarpPortableSourceExceptionLayout.InnerExceptionWord,
            TypeName = WarpPortableSourceExceptionLayout.TypeNameWord, HResult = WarpPortableSourceExceptionLayout.HResultWord,
            Failure = "one-context-cached-TypeInitializationException-owner-with-exact-original-inner-and-captured-type-name;source-resource-termination-never-wrapped",
            Search = "retain-cctor-original-source-frames-and-precise-roots-through-first-pass;compiled-initializer-boundary-before-caller-search",
            CachedTrace = "reuse-cached-wrapper-owner-and-inner-clear-wrapper-propagation-window-at-each-new-trigger-never-rerun-failed-cctor",
        });
    }

    internal string GraphHash { get; }
    internal string VerifiedHash { get; }
    internal string TypeSchemaHash { get; }
    internal string PlanHash { get; }
    internal ImmutableArray<WarpPortableSourceInitializerType> Types { get; }
    internal ImmutableArray<WarpPortableSourceInitializerTrigger> Triggers { get; }

    internal static WarpPortableSourceInitializerPlan Capture(WarpPortableMethodGraph graph, WarpPortableTypedProgram program,
        WarpPortableSourceHeapSchema schema)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(program); ArgumentNullException.ThrowIfNull(schema);
        if (!string.Equals(graph.GraphHash, program.GraphHash, StringComparison.Ordinal) ||
            !string.Equals(graph.GraphHash, schema.GraphHash, StringComparison.Ordinal) ||
            !string.Equals(program.VerifiedHash, schema.VerifiedHash, StringComparison.Ordinal))
        {
            throw new WarpVerificationException("WRPCLR2460", "Initializer triggers require the exact captured graph, verified program and dense heap schema.", 0);
        }
        uint wrapper = schema.TypeId(WarpPortableMethodGraphIdentity.Type(typeof(TypeInitializationException)));
        uint hresult = schema.ExceptionTypes[(int)wrapper - 1].DefaultHResult;
        var initializers = graph.Types.Where(type => type.Initializer is not null).OrderBy(type => type.Identity, StringComparer.Ordinal)
            .Select(type => new WarpPortableSourceInitializerType(schema.TypeId(type.Identity), type.Identity, type.Initializer!,
                type.SourceType.Attributes.HasFlag(System.Reflection.TypeAttributes.BeforeFieldInit), wrapper, hresult,
                WarpPortableTypeInitializerName.Capture(type.SourceType))).ToImmutableArray();
        var triggers = ImmutableArray.CreateBuilder<WarpPortableSourceInitializerTrigger>();
        if (program.EntryInitializerTrigger is { } entry)
        {
            // Root invocation precedes original CIL and has no source opcode/effect.
            // A private operation issuer must distinguish this entry trigger.
            triggers.Add(Row(graph.EntryIdentity, null, null, null, true, entry, schema));
        }
        foreach (WarpPortableTypedMethod method in program.Methods)
        {
            foreach (WarpPortableTypedInstruction instruction in method.Instructions.Where(instruction => instruction.Reachable))
            {
                int effect = instruction.Effects.IndexOf(WarpPortableTypedEffect.TypeInitialize);
                if (instruction.InitializerTrigger is not { } trigger)
                {
                    if (effect >= 0) { throw Invalid("A TypeInitialize effect lacks its exact captured initializer trigger.", instruction.Offset); }
                    continue;
                }
                if (effect < 0 || instruction.Effects.Count(item => item == WarpPortableTypedEffect.TypeInitialize) != 1 ||
                    !instruction.Faults.Any(fault => fault.Kind == WarpPortableTypedFaultKind.TypeInitialization && fault.EffectIndex == effect && fault.SourceOffset == instruction.Offset))
                {
                    throw Invalid("The captured initializer trigger lacks its exact ordered source fault/effect.", instruction.Offset);
                }
                triggers.Add(Row(method.Identity, instruction.Offset, unchecked((ushort)instruction.OpCode), effect, false, trigger, schema));
            }
        }
        return new(graph, program, schema, initializers, triggers.OrderBy(row => row.MethodIdentity, StringComparer.Ordinal)
            .ThenBy(row => row.EntryInvocation ? 0 : 1).ThenBy(row => row.SourceOffset).ToImmutableArray());
    }

    private static WarpPortableSourceInitializerTrigger Row(string method, int? offset, ushort? opcode, int? effect,
        bool entry, WarpPortableTypedInitializerTrigger trigger, WarpPortableSourceHeapSchema schema) =>
        new(method, offset, opcode, effect, entry, schema.TypeId(trigger.DeclaringType), trigger.Initializer,
            trigger.Kind, trigger.BeforeFieldInit, string.Equals(method, trigger.Initializer, StringComparison.Ordinal));

    private static WarpVerificationException Invalid(string message, int offset) => new("WRPCLR2460", message, offset);
}
