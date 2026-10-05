using System.Reflection.Emit;
using WarpCLR.IR;

namespace WarpCLR.Verifier;

internal static partial class WarpIntegerMapCilVerifier
{
    private static Dictionary<int, bool> AnalyzeArenaTypes(WarpIntegerMapMethodBody method, IReadOnlyList<CilBlock> blocks,
        WarpCilCompilationAdmission admission)
    {
        admission.AdmitTypeWorkspace(method, blocks.Count);
        var stateOperations = new Dictionary<int, bool>();
        var entries = new ArenaValueKind[]?[blocks.Count];
        entries[0] = [];
        var pending = new Queue<int>();
        pending.Enqueue(0);
        while (pending.TryDequeue(out int blockId))
        {
            var stack = new List<ArenaValueKind>(entries[blockId]!);
            foreach (DecodedInstruction instruction in blocks[blockId].Instructions)
            {
                ApplyArenaTypeInstruction(method, instruction, stack, stateOperations);
            }

            foreach (int successor in blocks[blockId].Successors)
            {
                if (entries[successor] is null)
                {
                    entries[successor] = stack.ToArray();
                    pending.Enqueue(successor);
                }
                else if (!stack.SequenceEqual(entries[successor]!))
                {
                    throw ArenaTypeError(blocks[successor].StartOffset);
                }
            }
        }
        return stateOperations;
    }

    private static void ApplyArenaTypeInstruction(WarpIntegerMapMethodBody method, DecodedInstruction instruction, List<ArenaValueKind> stack, Dictionary<int, bool> stateOperations)
    {
        if (ApplyArenaVariable(method, instruction, stack) || ApplyArenaOperation(instruction, stack))
        {
            return;
        }

        if (ApplyArenaMemory(instruction, stack, out bool state))
        {
            stateOperations[instruction.Offset] = state;
            return;
        }

        OpCode opCode = instruction.OpCode;
        if (Is(opCode, OpCodes.Call))
        {
            WarpCilCallTarget target = method.CallTargets[instruction.Operand];
            for (int argument = target.ParameterCount - 1; argument >= 0; argument--)
            {
                PopArenaType(stack, Kind(!target.ArenaParameters.IsDefault && target.ArenaParameters[argument],
                    !target.StateParameters.IsDefault && target.StateParameters[argument]), instruction.Offset);
            }

            stack.Add(ArenaValueKind.Word);
        }
        else if (IsConditionalBranch(opCode))
        {
            PopArenaType(stack, ArenaValueKind.Word, instruction.Offset);
            if (!IsBooleanBranch(opCode))
            {
                PopArenaType(stack, ArenaValueKind.Word, instruction.Offset);
            }
        }
        else if (Is(opCode, OpCodes.Ret))
        {
            PopArenaType(stack, ArenaValueKind.Word, instruction.Offset);
        }
        else if (!Is(opCode, OpCodes.Nop) && !IsUnconditionalBranch(opCode))
        {
            throw ArenaTypeError(instruction.Offset);
        }
    }

    private static bool ApplyArenaVariable(WarpIntegerMapMethodBody method, DecodedInstruction instruction, List<ArenaValueKind> stack)
    {
        if (TryGetArgumentIndex(instruction, out int argument))
        {
            stack.Add(Kind(method.ArenaParameters[argument], method.StateParameters[argument]));
        }
        else if (TryGetArgumentWriteIndex(instruction, out argument))
        {
            PopArenaType(stack, Kind(method.ArenaParameters[argument], method.StateParameters[argument]), instruction.Offset);
        }
        else if (TryGetConstant(instruction, out _) || TryGetLocalReadIndex(instruction, out _))
        {
            stack.Add(ArenaValueKind.Word);
        }
        else if (TryGetLocalWriteIndex(instruction, out _))
        {
            PopArenaType(stack, ArenaValueKind.Word, instruction.Offset);
        }
        else if (Is(instruction.OpCode, OpCodes.Dup))
        {
            RequireStack(stack.Count, 1, instruction.Offset);
            stack.Add(stack[^1]);
        }
        else if (Is(instruction.OpCode, OpCodes.Pop))
        {
            RequireStack(stack.Count, 1, instruction.Offset);
            stack.RemoveAt(stack.Count - 1);
        }
        else
        {
            return false;
        }

        return true;
    }

    private static bool ApplyArenaOperation(DecodedInstruction instruction, List<ArenaValueKind> stack)
    {
        OpCode opCode = instruction.OpCode;
        if (TryGetBinaryOperator(opCode, out _) || TryGetComparisonOperator(opCode, out _))
        {
            PopArenaType(stack, ArenaValueKind.Word, instruction.Offset);
            PopArenaType(stack, ArenaValueKind.Word, instruction.Offset);
            stack.Add(ArenaValueKind.Word);
        }
        else if (Is(opCode, OpCodes.Not) || Is(opCode, OpCodes.Conv_I4) || Is(opCode, OpCodes.Conv_U4))
        {
            PopArenaType(stack, ArenaValueKind.Word, instruction.Offset);
            stack.Add(ArenaValueKind.Word);
        }
        else
        {
            return false;
        }

        return true;
    }

    private static bool ApplyArenaMemory(DecodedInstruction instruction, List<ArenaValueKind> stack, out bool state)
    {
        state = false;
        OpCode opCode = instruction.OpCode;
        if (Is(opCode, OpCodes.Ldelema) || Is(opCode, OpCodes.Ldelem_U4))
        {
            PopArenaType(stack, ArenaValueKind.Word, instruction.Offset);
            state = PopBank(stack, address: false, instruction.Offset);
            stack.Add(Is(opCode, OpCodes.Ldelema) ? state ? ArenaValueKind.StateAddress : ArenaValueKind.Address : ArenaValueKind.Word);
        }
        else if (Is(opCode, OpCodes.Ldind_U4) || Is(opCode, OpCodes.Ldind_I4))
        {
            state = PopBank(stack, address: true, instruction.Offset);
            stack.Add(ArenaValueKind.Word);
        }
        else if (Is(opCode, OpCodes.Stind_I4) || Is(opCode, OpCodes.Stelem_I4))
        {
            PopArenaType(stack, ArenaValueKind.Word, instruction.Offset);
            bool address = Is(opCode, OpCodes.Stind_I4);
            if (!address) { PopArenaType(stack, ArenaValueKind.Word, instruction.Offset); }
            state = PopBank(stack, address, instruction.Offset);
        }
        else if (Is(opCode, OpCodes.Ldlen))
        {
            state = PopBank(stack, address: false, instruction.Offset);
            stack.Add(ArenaValueKind.Word);
        }
        else { return false; }
        return true;
    }

    private static bool PopBank(List<ArenaValueKind> stack, bool address, int offset)
    {
        RequireStack(stack.Count, 1, offset);
        ArenaValueKind kind = stack[^1];
        bool state = kind == (address ? ArenaValueKind.StateAddress : ArenaValueKind.State);
        if (!state && kind != (address ? ArenaValueKind.Address : ArenaValueKind.Arena))
        {
            throw ArenaTypeError(offset);
        }
        stack.RemoveAt(stack.Count - 1);
        return state;
    }

    private static void PopArenaType(List<ArenaValueKind> stack, ArenaValueKind expected, int offset)
    {
        RequireStack(stack.Count, 1, offset);
        if (stack[^1] != expected)
        {
            throw ArenaTypeError(offset);
        }

        stack.RemoveAt(stack.Count - 1);
    }

    private enum ArenaValueKind { Word, Arena, Address, State, StateAddress }

    private static ArenaValueKind Kind(bool arena, bool state) => state ? ArenaValueKind.State : arena ? ArenaValueKind.Arena : ArenaValueKind.Word;

    internal static IReadOnlyList<(int Token, int Offset)> ReadArenaElementTokens(ReadOnlySpan<byte> cil, string identity) =>
        Decode(cil, identity).Where(instruction => Is(instruction.OpCode, OpCodes.Ldelema))
            .Select(instruction => (instruction.Operand, instruction.Offset)).ToArray();

    private static WarpVerificationException ArenaTypeError(int offset) =>
        CilError("WRPCIL1017", "The CIL mixes a word value and the bound arena capability.", offset);

    private static bool LowerArenaInstruction(DecodedInstruction instruction, List<int> stack,
        List<WarpIrInstruction> lowered, WarpCilCompilationAdmission admission, ref int nextValue, bool stateBank)
    {
        OpCode opCode = instruction.OpCode;
        if (!Is(opCode, OpCodes.Ldlen) && !Is(opCode, OpCodes.Ldelem_U4) && !Is(opCode, OpCodes.Stelem_I4) &&
            !Is(opCode, OpCodes.Ldelema) && !Is(opCode, OpCodes.Ldind_U4) && !Is(opCode, OpCodes.Ldind_I4) && !Is(opCode, OpCodes.Stind_I4))
        {
            return false;
        }

        int result = TakeValue(ref nextValue, admission);
        if (Is(opCode, OpCodes.Ldlen))
        {
            _ = Pop(stack, instruction.Offset);
            lowered.Add(new WarpIrInstruction(result, stateBank ? WarpManagedStateOpCode.WordCount : WarpManagedMemoryOpCode.WordCount));
            stack.Add(result);
        }
        else if (Is(opCode, OpCodes.Ldind_U4) || Is(opCode, OpCodes.Ldind_I4))
        {
            int index = Pop(stack, instruction.Offset);
            lowered.Add(new WarpIrInstruction(result, stateBank ? WarpManagedStateOpCode.LoadWord : WarpManagedMemoryOpCode.LoadWord, left: index));
            stack.Add(result);
        }
        else if (Is(opCode, OpCodes.Ldelem_U4) || Is(opCode, OpCodes.Ldelema))
        {
            int index = Pop(stack, instruction.Offset);
            _ = Pop(stack, instruction.Offset);
            lowered.Add(new WarpIrInstruction(result,
                Is(opCode, OpCodes.Ldelema)
                    ? stateBank ? WarpManagedStateOpCode.WordAddress : WarpManagedMemoryOpCode.WordAddress
                    : stateBank ? WarpManagedStateOpCode.LoadWord : WarpManagedMemoryOpCode.LoadWord, left: index));
            stack.Add(result);
        }
        else
        {
            int value = Pop(stack, instruction.Offset);
            int index = Pop(stack, instruction.Offset);
            if (Is(opCode, OpCodes.Stelem_I4))
            {
                _ = Pop(stack, instruction.Offset);
            }

            lowered.Add(new WarpIrInstruction(result, stateBank ? WarpManagedStateOpCode.StoreWord : WarpManagedMemoryOpCode.StoreWord, index, value));
        }

        return true;
    }
}
