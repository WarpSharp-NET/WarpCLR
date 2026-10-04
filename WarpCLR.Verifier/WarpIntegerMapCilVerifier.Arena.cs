using System.Reflection.Emit;
using WarpCLR.IR;

namespace WarpCLR.Verifier;

internal static partial class WarpIntegerMapCilVerifier
{
    private static void AnalyzeArenaTypes(WarpIntegerMapMethodBody method, IReadOnlyList<CilBlock> blocks,
        WarpCilCompilationAdmission admission)
    {
        admission.AdmitTypeWorkspace(method, blocks.Count);
        var entries = new ArenaValueKind[]?[blocks.Count];
        entries[0] = [];
        var pending = new Queue<int>();
        pending.Enqueue(0);
        while (pending.TryDequeue(out int blockId))
        {
            var stack = new List<ArenaValueKind>(entries[blockId]!);
            foreach (DecodedInstruction instruction in blocks[blockId].Instructions)
            {
                ApplyArenaTypeInstruction(method, instruction, stack);
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
    }

    private static void ApplyArenaTypeInstruction(WarpIntegerMapMethodBody method, DecodedInstruction instruction, List<ArenaValueKind> stack)
    {
        if (ApplyArenaVariable(method, instruction, stack) || ApplyArenaMemory(instruction, stack) ||
            ApplyArenaOperation(instruction, stack))
        {
            return;
        }

        OpCode opCode = instruction.OpCode;
        if (Is(opCode, OpCodes.Call))
        {
            WarpCilCallTarget target = method.CallTargets[instruction.Operand];
            for (int argument = target.ParameterCount - 1; argument >= 0; argument--)
            {
                PopArenaType(stack, Kind(!target.ArenaParameters.IsDefault && target.ArenaParameters[argument]), instruction.Offset);
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
            stack.Add(Kind(method.ArenaParameters[argument]));
        }
        else if (TryGetArgumentWriteIndex(instruction, out argument))
        {
            PopArenaType(stack, Kind(method.ArenaParameters[argument]), instruction.Offset);
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

    private static bool ApplyArenaMemory(DecodedInstruction instruction, List<ArenaValueKind> stack)
    {
        OpCode opCode = instruction.OpCode;
        if (Is(opCode, OpCodes.Ldelema))
        {
            PopArenaType(stack, ArenaValueKind.Word, instruction.Offset);
            PopArenaType(stack, ArenaValueKind.Arena, instruction.Offset);
            stack.Add(ArenaValueKind.Address);
        }
        else if (Is(opCode, OpCodes.Ldind_U4) || Is(opCode, OpCodes.Ldind_I4))
        {
            PopArenaType(stack, ArenaValueKind.Address, instruction.Offset);
            stack.Add(ArenaValueKind.Word);
        }
        else if (Is(opCode, OpCodes.Stind_I4))
        {
            PopArenaType(stack, ArenaValueKind.Word, instruction.Offset);
            PopArenaType(stack, ArenaValueKind.Address, instruction.Offset);
        }
        else if (Is(opCode, OpCodes.Stelem_I4))
        {
            PopArenaType(stack, ArenaValueKind.Word, instruction.Offset);
            PopArenaType(stack, ArenaValueKind.Word, instruction.Offset);
            PopArenaType(stack, ArenaValueKind.Arena, instruction.Offset);
        }
        else if (Is(opCode, OpCodes.Ldelem_U4))
        {
            PopArenaType(stack, ArenaValueKind.Word, instruction.Offset);
            PopArenaType(stack, ArenaValueKind.Arena, instruction.Offset);
            stack.Add(ArenaValueKind.Word);
        }
        else if (Is(opCode, OpCodes.Ldlen))
        {
            PopArenaType(stack, ArenaValueKind.Arena, instruction.Offset);
            stack.Add(ArenaValueKind.Word);
        }
        else
        {
            return false;
        }

        return true;
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

    private enum ArenaValueKind { Word, Arena, Address }

    private static ArenaValueKind Kind(bool arena) => arena ? ArenaValueKind.Arena : ArenaValueKind.Word;

    internal static IReadOnlyList<(int Token, int Offset)> ReadArenaElementTokens(ReadOnlySpan<byte> cil, string identity) =>
        Decode(cil, identity).Where(instruction => Is(instruction.OpCode, OpCodes.Ldelema))
            .Select(instruction => (instruction.Operand, instruction.Offset)).ToArray();

    private static WarpVerificationException ArenaTypeError(int offset) =>
        CilError("WRPCIL1017", "The CIL mixes a word value and the bound arena capability.", offset);

    private static bool LowerArenaInstruction(DecodedInstruction instruction, List<int> stack,
        List<WarpIrInstruction> lowered, WarpCilCompilationAdmission admission, ref int nextValue)
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
            lowered.Add(new WarpIrInstruction(result, WarpManagedMemoryOpCode.WordCount));
            stack.Add(result);
        }
        else if (Is(opCode, OpCodes.Ldind_U4) || Is(opCode, OpCodes.Ldind_I4))
        {
            int index = Pop(stack, instruction.Offset);
            lowered.Add(new WarpIrInstruction(result, WarpManagedMemoryOpCode.LoadWord, left: index));
            stack.Add(result);
        }
        else if (Is(opCode, OpCodes.Ldelem_U4) || Is(opCode, OpCodes.Ldelema))
        {
            int index = Pop(stack, instruction.Offset);
            _ = Pop(stack, instruction.Offset);
            lowered.Add(new WarpIrInstruction(result,
                Is(opCode, OpCodes.Ldelema) ? WarpManagedMemoryOpCode.WordAddress : WarpManagedMemoryOpCode.LoadWord, left: index));
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

            lowered.Add(new WarpIrInstruction(result, WarpManagedMemoryOpCode.StoreWord, index, value));
        }

        return true;
    }
}
