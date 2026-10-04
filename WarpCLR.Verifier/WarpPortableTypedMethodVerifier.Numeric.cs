namespace WarpCLR.Verifier;

internal sealed partial class WarpPortableTypedMethodVerifier
{
    private void Numeric(WarpPortableMethodGraphInstruction instruction, WarpPortableTypedFlowState state, Step step)
    {
        string name = instruction.OpCode.Name!;
        if (name.StartsWith("conv.", StringComparison.Ordinal)) { Conversion(name, state, step); return; }
        WarpPortableTypedValue right = Pop(state, step.Offset);
        if (name is "neg" or "not")
        {
            Require(NumericCategory(right.Category) && (name is not "not" || Integer(right.Category)), "A unary numeric operation has an incompatible type.", step.Offset);
            state.Stack.Add(right with { SourceStorageType = null }); return;
        }

        WarpPortableTypedValue left = Pop(state, step.Offset);
        if (name is "ceq" or "cgt" or "cgt.un" or "clt" or "clt.un")
        {
            Comparison(left, right, name, step.Offset); state.Stack.Add(Primitive(typeof(int))); return;
        }

        if (name is "shl" or "shr" or "shr.un")
        {
            Require(Integer(left.Category) && right.Category == WarpPortableStackCategory.I4, "Shift requires an exact integer value and I4 count.", step.Offset);
            state.Stack.Add(left with { SourceStorageType = null }); return;
        }

        Require(NumericCategory(left.Category) && left.Category == right.Category, "Numeric operands have incompatible types or float widths.", step.Offset);
        if (name is "and" or "or" or "xor" || name.Contains("ovf", StringComparison.Ordinal) || name is "div.un" or "rem.un")
        {
            Require(Integer(left.Category), "A bitwise/checked/unsigned integer operation cannot consume float or reference values.", step.Offset);
        }

        if (name.Contains("ovf", StringComparison.Ordinal)) { step.Effects.Add(WarpPortableTypedEffect.OverflowCheck); }
        if (name is "div" or "div.un" or "rem" or "rem.un" && Integer(left.Category))
        {
            step.Effects.Add(WarpPortableTypedEffect.DivideByZeroCheck);
            if (name is "div" or "rem") { step.Effects.Add(WarpPortableTypedEffect.OverflowCheck); }
        }

        state.Stack.Add(types.Merge(left, right, step.Offset) with { SourceStorageType = null });
    }

    private void Conversion(string name, WarpPortableTypedFlowState state, Step step)
    {
        WarpPortableTypedValue value = Pop(state, step.Offset);
        Require(NumericCategory(value.Category), "A numeric conversion cannot reinterpret a managed reference/byref/handle.", step.Offset);
        string[] components = name.Split('.');
        string target = components[1] is "ovf" ? components[2] : components[1];
        Type? type = target switch
        {
            "i1" => typeof(sbyte), "u1" => typeof(byte), "i2" => typeof(short), "u2" => typeof(ushort),
            "i4" => typeof(int), "u4" => typeof(uint), "i8" => typeof(long), "u8" => typeof(ulong),
            "r4" => typeof(float), "r8" => typeof(double), "r" when name is "conv.r.un" => typeof(double), _ => null,
        };
        Require(type is not null, "A conversion targets an unsupported/native-size representation.", step.Offset);
        if (name is "conv.r.un") { Require(Integer(value.Category), "Conv.r.un requires an unsigned integer interpretation.", step.Offset); }
        if (components[1] is "ovf") { step.Effects.Add(WarpPortableTypedEffect.OverflowCheck); }
        step.Effects.Add(WarpPortableTypedEffect.Conversion); state.Stack.Add(LoadToStack(Primitive(type!)));
    }

    private void Comparison(WarpPortableTypedValue left, WarpPortableTypedValue right, string operation, int offset)
    {
        Require(!left.IsUninitializedThis && !right.IsUninitializedThis && left.Category == right.Category,
            "Comparison operands have incompatible categories or uninitialized constructor references.", offset);
        if (NumericCategory(left.Category)) { return; }
        if (left.Category == WarpPortableStackCategory.Reference)
        {
            Require(operation is "ceq" or "beq" or "bne.un" || operation is "cgt.un" && (left.IsNull || right.IsNull),
                "Reference ordering is outside the portable semantic contract.", offset); return;
        }

        Require(left.Category == WarpPortableStackCategory.ManagedByref && string.Equals(left.TypeIdentity, right.TypeIdentity, StringComparison.Ordinal) &&
            operation is "ceq" or "beq" or "bne.un", "Only equal-type managed byrefs can be compared for identity.", offset);
    }

    private static bool Integer(WarpPortableStackCategory category) => category is WarpPortableStackCategory.I4 or WarpPortableStackCategory.I8;
    private static bool NumericCategory(WarpPortableStackCategory category) => Integer(category) || category is WarpPortableStackCategory.Binary32 or WarpPortableStackCategory.Binary64;
}
