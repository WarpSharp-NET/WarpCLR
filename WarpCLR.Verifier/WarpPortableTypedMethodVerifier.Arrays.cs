using System.Reflection.Emit;

namespace WarpCLR.Verifier;

internal sealed partial class WarpPortableTypedMethodVerifier
{
    private void Array(WarpPortableMethodGraphInstruction instruction, WarpPortableTypedFlowState state, Step step)
    {
        OpCode operation = instruction.OpCode;
        if (operation == OpCodes.Newarr)
        {
            Require(Pop(state, step.Offset).Category == WarpPortableStackCategory.I4, "Array length must use the bounded I4 logical length type.", step.Offset);
            string array = types.Get(types.Source(instruction.Type!).MakeArrayType()).Identity;
            state.Stack.Add(types.Value(array)); step.Effects.Add(WarpPortableTypedEffect.OverflowCheck);
            step.Effects.Add(WarpPortableTypedEffect.Allocate); step.Effects.Add(WarpPortableTypedEffect.Safepoint); return;
        }

        if (operation == OpCodes.Ldlen)
        {
            WarpPortableTypedValue owner = Pop(state, step.Offset);
            Require(owner.Category == WarpPortableStackCategory.Reference && (owner.IsNull || types.Source(owner.TypeIdentity).IsArray),
                "Ldlen requires a managed array reference.", step.Offset);
            state.Stack.Add(Primitive(typeof(uint))); step.Effects.Add(WarpPortableTypedEffect.NullCheck); ReadEffect(step); return;
        }

        ArrayElement(instruction, state, step);
    }

    private void ArrayElement(WarpPortableMethodGraphInstruction instruction, WarpPortableTypedFlowState state, Step step)
    {
        OpCode operation = instruction.OpCode;
        bool store = instruction.OpCode.Name!.StartsWith("stelem", StringComparison.Ordinal);
        WarpPortableTypedValue? value = store ? Pop(state, step.Offset) : null;
        Require(Pop(state, step.Offset).Category == WarpPortableStackCategory.I4, "An array index must have I4 category.", step.Offset);
        WarpPortableTypedValue reference = Pop(state, step.Offset);
        Require(reference.Category == WarpPortableStackCategory.Reference && !reference.IsUninitializedThis &&
            (reference.IsNull || types.Source(reference.TypeIdentity).IsArray), "Array element access requires an exact managed array type.", step.Offset);
        string element = types.Get(reference.TypeIdentity).ElementType ?? instruction.Type ??
            ElementPrimitive(operation, types.Get(typeof(object)).Identity, step.Offset);
        string requested = instruction.Type ?? ElementPrimitive(instruction.OpCode, element, step.Offset);
        WarpPortableTypedType actual = types.Get(element);
        WarpPortableTypedType access = types.Get(requested);
        Require(string.Equals(requested, element, StringComparison.Ordinal) || actual.Category == access.Category && actual.ByteSize == access.ByteSize &&
            (Integer(actual.Category) || actual.Category == WarpPortableStackCategory.Reference), "Array opcode changes element category or width.", step.Offset);
        step.MemoryType = element; step.StorageBits = actual.StorageBits;
        step.Effects.Add(WarpPortableTypedEffect.NullCheck); step.Effects.Add(WarpPortableTypedEffect.BoundsCheck);
        if (operation == OpCodes.Ldelema)
        {
            if (!step.Prefix.ReadOnly) { step.Effects.Add(WarpPortableTypedEffect.TypeCheck); }
            state.Stack.Add(Borrow(requested, new(WarpPortableProvenanceKind.HeapInterior, string.Empty, -1,
                reference.TypeIdentity, 0, access.ByteSize), step.Prefix.ReadOnly)); return;
        }

        if (store)
        {
            RequireAssignable(value!, element, step.Offset);
            if (actual.Category == WarpPortableStackCategory.Reference) { step.Effects.Add(WarpPortableTypedEffect.TypeCheck); }
            WriteEffect(step);
        }
        else { ReadEffect(step); state.Stack.Add(LoadToStack(types.Value(actual.Category == WarpPortableStackCategory.Reference ? element : requested))); }
    }

    private string ElementPrimitive(OpCode operation, string element, int offset)
    {
        string suffix = operation.Name![(operation.Name!.IndexOf('.', StringComparison.Ordinal) + 1)..];
        if (suffix is "ref") { Require(types.Get(element).Category == WarpPortableStackCategory.Reference, "A reference opcode targets a nonreference element.", offset); return element; }
        Type? primitive = suffix switch
        {
            "i1" => typeof(sbyte), "u1" => typeof(byte), "i2" => typeof(short), "u2" => typeof(ushort),
            "i4" => typeof(int), "u4" => typeof(uint), "i8" => typeof(long), "r4" => typeof(float), "r8" => typeof(double), _ => null,
        };
        Require(primitive is not null, "A native-sized or unsupported element/indirect opcode cannot be retyped silently.", offset);
        return types.Get(primitive!).Identity;
    }
}
