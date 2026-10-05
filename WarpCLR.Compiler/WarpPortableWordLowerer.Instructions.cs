using System.Reflection.Emit;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableWordLowerer
{
    private sealed partial class MethodLowerer
    {
        private WarpBlockTerminator LowerInstruction(WarpPortableMethodGraphInstruction input, WarpPortableTypedInstruction typed)
        {
            OpCode operation = input.OpCode; string name = operation.Name!;
            if (operation == OpCodes.Ldlen) { throw Error("Ldlen has exact profile-bound native-unsigned evaluation width but still requires its generated heap read and operation-specific source fault/EH binding.", input.Offset); }
            if (operation == OpCodes.Ret) { return SourceReturn(typed); }
            if (operation.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch) { return Control(input, typed); }
            if (name.StartsWith("ldarg", StringComparison.Ordinal) || name.StartsWith("starg", StringComparison.Ordinal) ||
                name.StartsWith("ldloc", StringComparison.Ordinal) || name.StartsWith("stloc", StringComparison.Ordinal)) { PrivateStorage(input, typed); }
            else if (name.StartsWith("ldc.", StringComparison.Ordinal) || operation == OpCodes.Ldnull || operation == OpCodes.Dup || operation == OpCodes.Pop || operation == OpCodes.Sizeof) { Constants(input, typed); }
            else if (operation == OpCodes.Call || operation == OpCodes.Newobj) { Call(input, typed); }
            else if (operation == OpCodes.Ldfld) { ValueField(input, typed); }
            else if (name.StartsWith("conv.", StringComparison.Ordinal)) { Conversion(input, typed); }
            else if (name is "add" or "sub" or "mul" or "div" or "div.un" or "rem" or "rem.un" or "and" or "or" or "xor" or "shl" or "shr" or "shr.un" or "neg" or "not" or "ceq" or "cgt" or "cgt.un" or "clt" or "clt.un" || name.Contains("ovf", StringComparison.Ordinal)) { Numeric(input, typed); }
            else if (operation != OpCodes.Nop && operation.OpCodeType != OpCodeType.Prefix)
            {
                throw Error("The verified operation requires a generated runtime execution service: " + name, input.Offset);
            }
            return Next(typed);
        }

        private WarpBlockTerminator SourceReturn(WarpPortableTypedInstruction typed)
        {
            WarpPortableTypedType result = owner.Types[method.ReturnType];
            if (result.Category == WarpPortableStackCategory.Void) { return Return([]); }
            int[] words = StackValue(typed, 0).Take(result.WordCount).ToArray();
            if (result.Category == WarpPortableStackCategory.I4 && result.StorageBits < 32)
            {
                words[0] = Emit(WarpIrOpCode.BitwiseAnd, words[0], Constant((1u << result.StorageBits) - 1));
            }
            return Return(words);
        }

        private void Constants(WarpPortableMethodGraphInstruction input, WarpPortableTypedInstruction typed)
        {
            OpCode operation = input.OpCode;
            if (operation == OpCodes.Pop) { return; }
            int start = StackWord(typed, typed.EntryStack.Length);
            if (operation == OpCodes.Dup) { StoreWords(start, StackValue(typed, typed.EntryStack.Length - 1)); return; }
            if (operation == OpCodes.Ldnull) { StoreWords(start, [Constant(0), Constant(0), Constant(0)]); return; }
            if (operation == OpCodes.Sizeof)
            {
                WarpPortableTypedType type = owner.Types[input.Type!];
                uint size = owner.Program.CliSizes is { } cliSizes ? cliSizes.TypeSize(type.Identity, input.Offset).ByteSize :
                    type.Category is WarpPortableStackCategory.I4 or WarpPortableStackCategory.I8 or WarpPortableStackCategory.Binary32 or WarpPortableStackCategory.Binary64 ? (uint)type.ByteSize :
                    throw Error("Compound/reference sizeof requires its separate captured CLI numeric layout, never portable owner storage ByteSize.", input.Offset);
                Store(start, Constant(size)); return;
            }
            ulong bits = input.Operand;
            if (operation == OpCodes.Ldc_I4_M1) { bits = uint.MaxValue; }
            else if (operation.OperandType == OperandType.InlineNone) { bits = (uint)(operation.Name![^1] - '0'); }
            Store(start, Constant(unchecked((uint)bits)));
            if (typed.ExitStack[^1].WordCount == 2) { Store(start + 1, Constant((uint)(bits >> 32))); }
        }

        private int[] Invoke(Type implementation, string name, int[] operands, int results = 1)
        {
            int callee = owner.ImportService(implementation, name);
            if (results != 1) { throw new InvalidOperationException("A word arithmetic service has exactly one result."); }
            int result = value++; instructions.Add(new(result, callee, operands, 1)); return [result];
        }

        private int Service(Type implementation, string name, params int[] operands) => Invoke(implementation, name, operands)[0];
        private int[] WideService(Type implementation, string name, int[] operands) =>
            [Service(implementation, name + "Low", operands), Service(implementation, name + "High", operands)];
    }
}
