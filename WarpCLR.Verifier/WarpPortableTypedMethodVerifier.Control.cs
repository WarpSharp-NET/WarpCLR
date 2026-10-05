using System.Reflection;
using System.Reflection.Emit;

namespace WarpCLR.Verifier;

internal sealed partial class WarpPortableTypedMethodVerifier
{
    private void Control(int index, WarpPortableTypedFlowState state, Step step)
    {
        OpCode operation = method.Instructions[index].OpCode;
        if (operation == OpCodes.Ret) { Return(state, step); return; }
        if (operation == OpCodes.Throw)
        {
            WarpPortableTypedValue value = Pop(state, step.Offset);
            Require(value.Category == WarpPortableStackCategory.Reference && !value.IsUninitializedThis &&
                (value.IsNull || typeof(Exception).IsAssignableFrom(types.Source(value.TypeIdentity))), "Throw requires a portable exception reference.", step.Offset);
            Require(state.Stack.Count == 0, "Throw leaves extraneous stack values.", step.Offset);
            step.Effects.Add(WarpPortableTypedEffect.NullCheck);
            step.Effects.Add(WarpPortableTypedEffect.Call); return;
        }

        if (operation == OpCodes.Rethrow || operation == OpCodes.Endfinally)
        {
            Require(state.Stack.Count == 0 && memberships[index].Any(member => operation == OpCodes.Rethrow ?
                member.Role == WarpPortableExceptionRole.Catch : member.Role is WarpPortableExceptionRole.Finally or WarpPortableExceptionRole.Fault),
                "Rethrow/endfinally occurs outside its handler or with a nonempty stack.", step.Offset);
            step.Effects.Add(operation == OpCodes.Rethrow ? WarpPortableTypedEffect.Call : WarpPortableTypedEffect.Control); return;
        }

        if (operation == OpCodes.Endfilter)
        {
            Require(Pop(state, step.Offset).Category == WarpPortableStackCategory.I4 && state.Stack.Count == 0,
                "An exception filter must produce exactly one I4 decision.", step.Offset); return;
        }

        if (operation == OpCodes.Leave || operation == OpCodes.Leave_S) { state.Stack.Clear(); step.Effects.Add(WarpPortableTypedEffect.Control); return; }
        if (operation == OpCodes.Switch)
        {
            Require(Pop(state, step.Offset).Category == WarpPortableStackCategory.I4, "Switch requires an I4 selector.", step.Offset);
        }
        else if (operation.FlowControl == FlowControl.Cond_Branch) { Branch(operation.Name!, state, step); }
        else { Require(operation.FlowControl == FlowControl.Branch, "No typed rule exists for this instruction.", step.Offset); }
        step.Effects.Add(WarpPortableTypedEffect.Control);
        if (successors[index].Any(successor => successor <= index)) { step.Effects.Add(WarpPortableTypedEffect.Safepoint); }
    }

    private void Branch(string name, WarpPortableTypedFlowState state, Step step)
    {
        string operation = name.EndsWith(".s", StringComparison.Ordinal) ? name[..^2] : name;
        WarpPortableTypedValue right = Pop(state, step.Offset);
        if (operation is "brtrue" or "brfalse")
        {
            Require(!right.IsUninitializedThis && (Integer(right.Category) || right.Category is WarpPortableStackCategory.Reference or WarpPortableStackCategory.ManagedByref),
                "A conditional branch cannot consume an incompatible/uninitialized value.", step.Offset); return;
        }

        WarpPortableTypedValue left = Pop(state, step.Offset);
        string comparison = operation is "beq" or "bne.un" ? operation : operation.EndsWith(".un", StringComparison.Ordinal) ? "cgt.un" : "cgt";
        Comparison(left, right, comparison, step.Offset);
    }

    private void Return(WarpPortableTypedFlowState state, Step step)
    {
        if (types.Get(method.ReturnType).Category != WarpPortableStackCategory.Void)
        {
            WarpPortableTypedValue value = Pop(state, step.Offset);
            RequireAssignable(value, method.ReturnType, step.Offset);
            if (value.Category == WarpPortableStackCategory.ManagedByref)
            {
                RequirePointer(value, null, step.Offset);
                Require(!validateLifetimes || value.Provenance.All(origin => origin.Kind is not (WarpPortableProvenanceKind.FrameLocal or WarpPortableProvenanceKind.FrameArgument)),
                    "A byref to private frame storage escapes its owner's lifetime at return.", step.Offset);
                returned.UnionWith(value.Provenance); returnedReadOnly |= value.IsReadOnly;
                Require(!value.IsReadOnly || method.SourceMethod is MethodInfo function && ReadOnlyParameter(function.ReturnParameter),
                    "A readonly byref cannot escape through a mutable ref-return signature.", step.Offset);
            }
        }

        Require(state.Stack.Count == 0 && state.InitializedThis, "Return has extra values or an uninitialized constructor this.", step.Offset);
        ParameterInfo[] parameters = method.SourceMethod.GetParameters();
        for (int index = 0; index < state.Arguments.Length; index++)
        {
            int parameter = index - (method.SourceMethod.IsStatic ? 0 : 1);
            bool output = parameter >= 0 ? parameters[parameter].IsOut && !parameters[parameter].IsIn :
                method.SourceMethod is ConstructorInfo && method.SourceMethod.DeclaringType!.IsValueType;
            if (output) { Require(state.InitializedPointees[index].All(initialized => initialized), "A normal return leaves an out/constructor pointee uninitialized.", step.Offset); }
        }

        step.MemoryType = method.ReturnType; step.StorageBits = types.Get(method.ReturnType).StorageBits;
        step.Effects.Add(WarpPortableTypedEffect.Return);
    }
}
