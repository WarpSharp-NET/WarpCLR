using System.Reflection.Emit;

namespace WarpCLR.Verifier;

internal sealed partial class WarpPortableTypedMethodVerifier
{
    private void Indirect(WarpPortableMethodGraphInstruction instruction, WarpPortableTypedFlowState state, Step step)
    {
        OpCode operation = instruction.OpCode;
        if (operation == OpCodes.Cpobj)
        {
            WarpPortableTypedValue source = Pop(state, step.Offset);
            WarpPortableTypedValue destination = Pop(state, step.Offset);
            RequirePointer(source, instruction.Type, step.Offset); RequirePointer(destination, instruction.Type, step.Offset);
            RequireInitialized(source, state, step.Offset); WriteBorrow(destination, state, step.Offset);
            step.Effects.Add(WarpPortableTypedEffect.NullCheck); ReadEffect(step); WriteEffect(step);
            step.MemoryType = instruction.Type; return;
        }

        bool initialize = operation == OpCodes.Initobj;
        bool store = initialize || operation == OpCodes.Stobj || operation.Name!.StartsWith("stind.", StringComparison.Ordinal);
        WarpPortableTypedValue? value = store && !initialize ? Pop(state, step.Offset) : null;
        WarpPortableTypedValue pointer = Pop(state, step.Offset);
        RequirePointer(pointer, null, step.Offset);
        string element = types.Get(pointer.TypeIdentity).ElementType!;
        string requested = instruction.Type ?? ElementPrimitive(operation, element, step.Offset);
        WarpPortableTypedType target = types.Get(element);
        WarpPortableTypedType access = types.Get(requested);
        Require(string.Equals(element, requested, StringComparison.Ordinal) || target.Category == access.Category && target.ByteSize == access.ByteSize && Integer(target.Category),
            "An indirect opcode changes the byref element width/category.", step.Offset);
        step.MemoryType = element; step.StorageBits = target.StorageBits;
        step.Effects.Add(WarpPortableTypedEffect.NullCheck);
        if (store)
        {
            if (value is not null) { RequireAssignable(value, element, step.Offset); }
            WriteBorrow(pointer, state, step.Offset); WriteEffect(step);
        }
        else
        {
            RequireInitialized(pointer, state, step.Offset); ReadEffect(step); state.Stack.Add(LoadToStack(types.Value(requested)));
        }
    }

    private void Object(WarpPortableMethodGraphInstruction instruction, WarpPortableTypedFlowState state, Step step)
    {
        OpCode operation = instruction.OpCode;
        string identity = instruction.Type!;
        WarpPortableTypedType type = types.Get(identity);
        WarpPortableTypedValue value = Pop(state, step.Offset);
        if (operation == OpCodes.Box)
        {
            Require(type.Category != WarpPortableStackCategory.ManagedByref && !types.Source(identity).IsByRefLike, "A managed byref/byref-like value cannot escape by boxing.", step.Offset);
            RequireAssignable(value, identity, step.Offset);
            string boxed = WarpPortableMethodGraphIntrinsics.IsStructuralNullable(types.Source(identity)) ?
                types.Get(types.Source(identity).GetGenericArguments()[0]).Identity : identity;
            state.Stack.Add(value with { TypeIdentity = boxed, Category = WarpPortableStackCategory.Reference, WordCount = 3, Provenance = [] });
            if (type.Category != WarpPortableStackCategory.Reference)
            {
                step.Effects.Add(WarpPortableTypedEffect.Allocate); step.Effects.Add(WarpPortableTypedEffect.Safepoint);
            }

            return;
        }

        Require(value.Category == WarpPortableStackCategory.Reference && !value.IsUninitializedThis, "A cast/unbox requires an initialized managed reference.", step.Offset);
        if (operation == OpCodes.Unbox)
        {
            Require(type.Category != WarpPortableStackCategory.Reference, "Unbox requires an exact value-type target.", step.Offset);
            bool nullable = WarpPortableMethodGraphIntrinsics.IsStructuralNullable(types.Source(identity));
            if (!nullable) { step.Effects.Add(WarpPortableTypedEffect.NullCheck); }
            step.Effects.Add(WarpPortableTypedEffect.TypeCheck);
            if (nullable)
            {
                step.Effects.Add(WarpPortableTypedEffect.Allocate); step.Effects.Add(WarpPortableTypedEffect.Safepoint);
            }
            state.Stack.Add(Borrow(identity, new(WarpPortableProvenanceKind.HeapInterior, string.Empty, -1, identity, 0, type.ByteSize))); return;
        }

        if (operation == OpCodes.Unbox_Any && type.Category != WarpPortableStackCategory.Reference)
        {
            if (!WarpPortableMethodGraphIntrinsics.IsStructuralNullable(types.Source(identity))) { step.Effects.Add(WarpPortableTypedEffect.NullCheck); }
            step.Effects.Add(WarpPortableTypedEffect.TypeCheck);
            ReadEffect(step); state.Stack.Add(LoadToStack(types.Value(identity))); return;
        }

        if (operation != OpCodes.Isinst) { step.Effects.Add(WarpPortableTypedEffect.TypeCheck); }
        string referenceType = WarpPortableMethodGraphIntrinsics.IsStructuralNullable(types.Source(identity)) ?
            types.Get(types.Source(identity).GetGenericArguments()[0]).Identity : identity;
        state.Stack.Add(types.Value(referenceType) with { Category = WarpPortableStackCategory.Reference, WordCount = 3, IsNull = value.IsNull });
    }
}
