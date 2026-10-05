using System.Reflection;
using System.Reflection.Emit;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableClosedFrameSourcePlan
{
    private void CheckInstruction(string methodIdentity, WarpPortableMethodGraphInstruction original, WarpPortableTypedInstruction typed)
    {
        string name = original.OpCode.Name!;
        if (typed.Effects.Any(effect => effect is WarpPortableTypedEffect.Acquire or WarpPortableTypedEffect.Release or WarpPortableTypedEffect.AtomicSequential) ||
            typed.EntryStack.Concat(typed.ExitStack).Any(value => value.Category == WarpPortableStackCategory.FunctionTarget ||
                value.Category == WarpPortableStackCategory.ManagedByref && value.Provenance.Any(origin =>
                    origin.Kind is not (WarpPortableProvenanceKind.Argument or WarpPortableProvenanceKind.FrameArgument or WarpPortableProvenanceKind.FrameLocal))))
        {
            throw Invalid("Only ordinary, internal, nonescaping source-frame owner provenance is bound here.", typed.Offset);
        }
        if (typed.ExceptionMemberships.Any(member => member.Role == WarpPortableExceptionRole.Filter) &&
            (typed.EntryStack.Concat(typed.ExitStack).Any(value => value.Category == WarpPortableStackCategory.ManagedByref) ||
             original.OpCode == OpCodes.Newobj && typed.RequiredIntrinsic?.Contains("value-tuple.construct", StringComparison.Ordinal) != true))
        {
            throw Invalid("A filter alias cannot mint or use a frame owner without its separate original-prefix/tail address capability.", typed.Offset);
        }
        // A concrete sealed EH binding owns the complete captured instruction,
        // including provenance, masks, ordered effects and protected regions.
        // It deliberately owns no newobj and grants no private throw/factory domain.
        if (exceptions?.OwnsInstruction(methodIdentity, typed) == true) { return; }
        if (original.Field is { } field)
        {
            WarpPortableMethodGraphField metadata = Graph.Fields.First(item => string.Equals(item.Identity, field, StringComparison.Ordinal));
            int receiver = typed.EntryStack.Length - (original.OpCode == OpCodes.Stfld ? 2 : 1);
            if (metadata.IsStatic || receiver < 0 || typed.EntryStack[receiver].Category == WarpPortableStackCategory.Reference)
            {
                throw Invalid("Heap/static source fields require their real owner/initializer/fault binding.", typed.Offset);
            }
        }
        if (original.Method is { } method)
        {
            if (original.OpCode != OpCodes.Call && original.OpCode != OpCodes.Newobj) { throw Invalid("Indirect/virtual/delegate source calls require real dispatch admission.", typed.Offset); }
            CheckCall(methods[method], original);
        }
        bool indirect = name.StartsWith("ldind.", StringComparison.Ordinal) || name.StartsWith("stind.", StringComparison.Ordinal) ||
            original.OpCode == OpCodes.Ldobj || original.OpCode == OpCodes.Stobj || original.OpCode == OpCodes.Initobj || original.OpCode == OpCodes.Cpobj;
        foreach (WarpPortableTypedFault fault in typed.Faults)
        {
            if (fault.Kind == WarpPortableTypedFaultKind.CalledException || fault.Kind == WarpPortableTypedFaultKind.TypeInitialization && original.Method is not null ||
                fault.Kind == WarpPortableTypedFaultKind.NullReference && indirect) { continue; }
            throw Invalid("The source operation needs an actual operation-specific implicit exception binding.", typed.Offset);
        }
        if (original.OpCode == OpCodes.Ldflda || original.OpCode == OpCodes.Stfld || indirect) { return; }
        if (typed.MemoryType is { } memory && types[memory].Category == WarpPortableStackCategory.ManagedByref &&
            name.StartsWith("st", StringComparison.Ordinal)) { return; }
        if (name.StartsWith("ldelem", StringComparison.Ordinal) || name.StartsWith("stelem", StringComparison.Ordinal) ||
            original.OpCode is var operation && (operation == OpCodes.Ldlen || operation == OpCodes.Newarr || operation == OpCodes.Ldstr ||
            operation == OpCodes.Box || operation == OpCodes.Unbox || operation == OpCodes.Unbox_Any || operation == OpCodes.Castclass || operation == OpCodes.Isinst))
        {
            throw Invalid("Source heap/array/string/boxing/type operations require their real service/factory binding.", typed.Offset);
        }
    }

    private void CheckCall(WarpPortableMethodGraphMethod target, WarpPortableMethodGraphInstruction input)
    {
        WarpPortableMethodGraphType declaring = Graph.Types.First(type => type.SourceType == target.SourceMethod.DeclaringType);
        if (declaring.Initializer is not null || input.OpCode == OpCodes.Newobj && !target.SourceMethod.DeclaringType!.IsValueType)
        {
            throw Invalid("This source constructor/call requires allocation or initializer admission.", input.Offset);
        }
        if (target.Intrinsic is not { } intrinsic) { return; }
        if (intrinsic.Contains("numeric.bit-cast.", StringComparison.Ordinal) || intrinsic.Contains("value-tuple.construct", StringComparison.Ordinal) ||
            intrinsic.Contains("object.reference-equality", StringComparison.Ordinal)) { return; }
        if (target.SourceMethod is MethodInfo method && WarpPortableWordMathCatalog.Resolve(method) is { FaultEntrypoint: null }) { return; }
        throw Invalid("The exact runtime intrinsic needs an independently admitted execution/fault binding.", input.Offset);
    }
}
