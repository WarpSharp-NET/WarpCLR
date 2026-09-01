using System.Collections.ObjectModel;

namespace WarpCLR.IR;

public enum WarpIrOpCode
{
    LoadInput,
    LoadScalar,
    Constant,
    BitwiseNot,
    Add,
    Subtract,
    Multiply,
    BitwiseAnd,
    BitwiseOr,
    ExclusiveOr,
    ShiftLeft,
    ShiftRightLogical,
}

public readonly record struct WarpIrInstruction(
    int Result,
    WarpIrOpCode OpCode,
    int Left = -1,
    int Right = -1,
    uint Immediate = 0);

public sealed class WarpLinearKernel
{
    public WarpLinearKernel(
        string name,
        int inputBufferCount,
        int scalarArgumentCount,
        IEnumerable<WarpIrInstruction> instructions,
        int result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(inputBufferCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(scalarArgumentCount);
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentOutOfRangeException.ThrowIfNegative(result);

        WarpIrInstruction[] instructionArray = instructions.ToArray();
        if ((uint)result >= (uint)instructionArray.Length)
        {
            throw new ArgumentException("The instruction sequence does not define the result.", nameof(instructions));
        }

        for (int index = 0; index < instructionArray.Length; index++)
        {
            ValidateInstruction(
                instructionArray[index],
                index,
                inputBufferCount,
                scalarArgumentCount);
        }

        Name = name;
        InputBufferCount = inputBufferCount;
        ScalarArgumentCount = scalarArgumentCount;
        Instructions = Array.AsReadOnly(instructionArray);
        Result = result;
    }

    public string Name { get; }

    public int InputBufferCount { get; }

    public int ScalarArgumentCount { get; }

    public ReadOnlyCollection<WarpIrInstruction> Instructions { get; }

    public int Result { get; }

    public int ValueCount => Instructions.Count;

    private static void ValidateInstruction(
        WarpIrInstruction instruction,
        int index,
        int inputBufferCount,
        int scalarArgumentCount)
    {
        if (instruction.Result != index)
        {
            throw new ArgumentException("Instruction result identifiers must be sequential.");
        }

        switch (instruction.OpCode)
        {
            case WarpIrOpCode.LoadInput:
                RequireNoOperands(instruction);
                if (instruction.Immediate >= (uint)inputBufferCount)
                {
                    throw new ArgumentException("An instruction references an invalid input buffer.");
                }

                break;

            case WarpIrOpCode.LoadScalar:
                RequireNoOperands(instruction);
                if (instruction.Immediate >= (uint)scalarArgumentCount)
                {
                    throw new ArgumentException("An instruction references an invalid scalar argument.");
                }

                break;

            case WarpIrOpCode.Constant:
                RequireNoOperands(instruction);
                break;

            case WarpIrOpCode.BitwiseNot:
                RequirePriorValue(instruction.Left, index);
                if (instruction.Right != -1)
                {
                    throw new ArgumentException("A unary instruction has an unexpected right operand.");
                }

                break;

            case WarpIrOpCode.Add:
            case WarpIrOpCode.Subtract:
            case WarpIrOpCode.Multiply:
            case WarpIrOpCode.BitwiseAnd:
            case WarpIrOpCode.BitwiseOr:
            case WarpIrOpCode.ExclusiveOr:
            case WarpIrOpCode.ShiftLeft:
            case WarpIrOpCode.ShiftRightLogical:
                RequirePriorValue(instruction.Left, index);
                RequirePriorValue(instruction.Right, index);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(instruction),
                    instruction.OpCode,
                    "The instruction opcode is not registered.");
        }
    }

    private static void RequireNoOperands(WarpIrInstruction instruction)
    {
        if (instruction.Left != -1 || instruction.Right != -1)
        {
            throw new ArgumentException("A load or constant instruction has unexpected operands.");
        }
    }

    private static void RequirePriorValue(int operand, int result)
    {
        if ((uint)operand >= (uint)result)
        {
            throw new ArgumentException("An instruction operand must reference a prior result.");
        }
    }
}
