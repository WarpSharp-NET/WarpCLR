using System.Collections.Immutable;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

// A captured operation identity, never an execution or factory authority token.
internal sealed class WarpPortableSourceOperationOrigin
{
    internal const string Semantics = "warp.source-operation-origin/exact-instruction-or-before-body-entry-invocation-no-fabricated-il-effect-or-eh-membership/0.1";

    private WarpPortableSourceOperationOrigin(WarpPortableSourceInitializerPlan plan, WarpPortableSourceInitializerTrigger trigger,
        string capturedSourceHash, ImmutableArray<WarpPortableTypedEffect> effects,
        ImmutableArray<WarpPortableTypedExceptionMembership> memberships)
    {
        Kind = trigger.EntryInvocation ? WarpPortableSourceOperationOriginKind.EntryInvocation : WarpPortableSourceOperationOriginKind.Instruction;
        MethodIdentity = trigger.MethodIdentity; SourceOffset = trigger.SourceOffset; SourceOpCode = trigger.SourceOpCode;
        EffectIndex = trigger.EffectIndex; CapturedSourceHash = capturedSourceHash; Effects = effects; ExceptionMemberships = memberships;
        OriginHash = WarpPortableSnapshotIdentity.Hash(new
        {
            Semantics, Kind, MethodIdentity, SourceOffset, SourceOpCode, EffectIndex, CapturedSourceHash, Effects, ExceptionMemberships,
            plan.GraphHash, plan.VerifiedHash, plan.TypeSchemaHash, plan.PlanHash, trigger,
            Invocation = "before-original-body;retain-private-arguments-and-raw-census;no-target-body-catch-membership;final-sealed-prelude-binding-required",
        });
    }

    internal WarpPortableSourceOperationOriginKind Kind { get; }
    internal string MethodIdentity { get; }
    internal int? SourceOffset { get; }
    internal ushort? SourceOpCode { get; }
    internal int? EffectIndex { get; }
    internal string CapturedSourceHash { get; }
    internal string OriginHash { get; }
    internal ImmutableArray<WarpPortableTypedEffect> Effects { get; }
    internal ImmutableArray<WarpPortableTypedExceptionMembership> ExceptionMemberships { get; }

    internal static WarpPortableSourceOperationOrigin CaptureInitializer(WarpPortableMethodGraph graph,
        WarpPortableTypedProgram program, WarpPortableSourceInitializerPlan plan, WarpPortableSourceInitializerTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(trigger);
        if (!string.Equals(graph.GraphHash, program.GraphHash, StringComparison.Ordinal) ||
            !string.Equals(plan.GraphHash, graph.GraphHash, StringComparison.Ordinal) ||
            !string.Equals(plan.VerifiedHash, program.VerifiedHash, StringComparison.Ordinal) || !plan.Triggers.Contains(trigger))
        {
            throw Invalid("An initializer operation must belong to the exact immutable captured trigger plan.", trigger.SourceOffset);
        }
        WarpPortableTypedMethod method = program.Methods.First(method => string.Equals(method.Identity, trigger.MethodIdentity, StringComparison.Ordinal));
        if (trigger.EntryInvocation)
        {
            if (!string.Equals(trigger.MethodIdentity, graph.EntryIdentity, StringComparison.Ordinal) || trigger.SourceOffset is not null ||
                trigger.SourceOpCode is not null || trigger.EffectIndex is not null || program.EntryInitializerTrigger is not { } entry ||
                !string.Equals(entry.Initializer, trigger.Initializer, StringComparison.Ordinal))
            {
                throw Invalid("Root initialization precedes the original body and cannot invent an IL opcode, effect or handler membership.", trigger.SourceOffset);
            }
            string source = WarpPortableSnapshotIdentity.Hash(new
            {
                method.Identity, method.ArgumentTypes, method.LocalTypes, method.ReturnType,
                EntryTrigger = entry, OriginalEntry = graph.EntryIdentity, Origin = Semantics,
            });
            return new(plan, trigger, source, [], []);
        }
        if (trigger.SourceOffset is not { } offset || trigger.SourceOpCode is not { } opcode || trigger.EffectIndex is not { } effect)
        {
            throw Invalid("An instruction initializer origin requires its real IL offset, opcode and ordered effect.", trigger.SourceOffset);
        }
        WarpPortableTypedInstruction? instruction = method.Instructions.FirstOrDefault(instruction => instruction.Reachable && instruction.Offset == offset);
        if (instruction is null || unchecked((ushort)instruction.OpCode) != opcode || (uint)effect >= (uint)instruction.Effects.Length ||
            instruction.Effects[effect] != WarpPortableTypedEffect.TypeInitialize || instruction.InitializerTrigger is not { } exact ||
            !string.Equals(exact.Initializer, trigger.Initializer, StringComparison.Ordinal))
        {
            throw Invalid("An initializer instruction origin must match its full verified original source instruction.", offset);
        }
        return new(plan, trigger, WarpPortableSnapshotIdentity.Hash(instruction), instruction.Effects, instruction.ExceptionMemberships);
    }

    private static WarpVerificationException Invalid(string message, int? offset) => new("WRPCLR2490", message, offset ?? 0);
}
