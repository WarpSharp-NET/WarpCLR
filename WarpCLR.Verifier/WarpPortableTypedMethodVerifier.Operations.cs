using System.Reflection.Emit;

namespace WarpCLR.Verifier;

internal sealed partial class WarpPortableTypedMethodVerifier
{
    private void Evaluate(int index, WarpPortableTypedFlowState state, Step step)
    {
        WarpPortableMethodGraphInstruction instruction = method.Instructions[index];
        OpCode operation = instruction.OpCode;
        string name = operation.Name!;
        if (operation == OpCodes.Nop || operation.OpCodeType == OpCodeType.Prefix) { return; }
        if (name.StartsWith("ldarg", StringComparison.Ordinal) || name.StartsWith("starg", StringComparison.Ordinal) ||
            name.StartsWith("ldloc", StringComparison.Ordinal) || name.StartsWith("stloc", StringComparison.Ordinal))
        {
            Storage(instruction, state, step); return;
        }

        if (name.StartsWith("ldc.", StringComparison.Ordinal) || operation == OpCodes.Ldnull || operation == OpCodes.Ldstr ||
            operation == OpCodes.Ldtoken || operation == OpCodes.Sizeof || operation == OpCodes.Dup || operation == OpCodes.Pop)
        {
            Constants(instruction, state, step); return;
        }

        if (NumericName(name)) { Numeric(instruction, state, step); return; }
        if (operation == OpCodes.Call || operation == OpCodes.Callvirt || operation == OpCodes.Newobj ||
            operation == OpCodes.Ldftn || operation == OpCodes.Ldvirtftn)
        {
            Call(instruction, state, step); return;
        }

        if (operation == OpCodes.Ldfld || operation == OpCodes.Ldflda || operation == OpCodes.Stfld || operation == OpCodes.Ldsfld ||
            operation == OpCodes.Ldsflda || operation == OpCodes.Stsfld) { Field(instruction, state, step); return; }
        if (ArrayName(name)) { Array(instruction, state, step); return; }
        if (MemoryOperation(operation) || operation == OpCodes.Initobj || operation == OpCodes.Cpobj)
        {
            Indirect(instruction, state, step); return;
        }

        if (operation == OpCodes.Box || operation == OpCodes.Unbox || operation == OpCodes.Unbox_Any ||
            operation == OpCodes.Castclass || operation == OpCodes.Isinst) { Object(instruction, state, step); return; }
        Control(index, state, step);
    }

    private static bool NumericName(string name) => name is "add" or "add.ovf" or "add.ovf.un" or "sub" or "sub.ovf" or "sub.ovf.un" or
        "mul" or "mul.ovf" or "mul.ovf.un" or "div" or "div.un" or "rem" or "rem.un" or "and" or "or" or "xor" or
        "shl" or "shr" or "shr.un" or "neg" or "not" or "ceq" or "cgt" or "cgt.un" or "clt" or "clt.un" ||
        name.StartsWith("conv.", StringComparison.Ordinal);

    private static bool ArrayName(string name) => name is "newarr" or "ldlen" or "ldelema" or "ldelem" or "stelem" ||
        name.StartsWith("ldelem.", StringComparison.Ordinal) || name.StartsWith("stelem.", StringComparison.Ordinal);

    private static bool Known(OpCode operation)
    {
        string? name = operation.Name;
        return name is not null && (NumericName(name) || ArrayName(name) || MemoryOperation(operation) ||
            name is "nop" or "ldnull" or "ldstr" or "ldtoken" or "sizeof" or "dup" or "pop" or "initobj" or "cpobj" or
            "call" or "callvirt" or "newobj" or "ldftn" or "ldvirtftn" or "ldfld" or "ldflda" or "stfld" or "ldsfld" or "ldsflda" or "stsfld" or
            "box" or "unbox" or "unbox.any" or "castclass" or "isinst" or "ret" or "throw" or "rethrow" or "endfinally" or "endfilter" or
            "leave" or "leave.s" or "switch" or "constrained." or "readonly." or "volatile." or "unaligned." ||
            name.StartsWith("ldarg", StringComparison.Ordinal) || name.StartsWith("starg", StringComparison.Ordinal) ||
            name.StartsWith("ldloc", StringComparison.Ordinal) || name.StartsWith("stloc", StringComparison.Ordinal) || name.StartsWith("ldc.", StringComparison.Ordinal) ||
            operation.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch);
    }
}
