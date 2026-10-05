using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace WarpCLR.IR;

public readonly record struct WarpIrInstruction
{
    private readonly ReadOnlyCollection<int>? arguments;
    private readonly int resultWordCountDelta;

    internal WarpIrInstruction(int result, int callee, IEnumerable<int> arguments, int resultWordCount)
        : this(result, WarpIrOpCode.Call, callee: callee, arguments: arguments)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(resultWordCount);
        WarpCompilationAdmission.Require("<IR-call>", WarpCompilationResourceKind.Parameters,
            resultWordCount, WarpCompilationAdmission.MaximumParametersPerBody);
        resultWordCountDelta = resultWordCount - 1;
    }

    internal WarpIrInstruction(int result, WarpIrOpCode opCode, int address, IEnumerable<int> operands, int resultWordCount)
        : this(result, opCode, address, arguments: operands)
    {
        if (!WarpManagedWideAtomicOpCode.IsAtomic(opCode) || resultWordCount != 2)
        {
            throw new ArgumentException("A wide atomic defines exactly two adjacent UInt32 result words.", nameof(opCode));
        }
        resultWordCountDelta = 1;
    }

    internal int ResultWordCount => resultWordCountDelta + 1;

    public WarpIrInstruction(
        int result,
        WarpIrOpCode opCode,
        int left = -1,
        int right = -1,
        uint immediate = 0,
        int third = -1,
        WarpIrValueType resultType = WarpIrValueType.UInt32,
        int callee = -1,
        IEnumerable<int>? arguments = null)
    {
        Result = result;
        ResultType = resultType;
        OpCode = opCode;
        Left = left;
        Right = right;
        Immediate = immediate;
        Third = third;
        Callee = callee;
        this.arguments = Array.AsReadOnly(arguments is null ? [] : WarpCompilationAdmission.Materialize(arguments,
            "<IR-instruction>", WarpCompilationResourceKind.Parameters, WarpCompilationAdmission.MaximumParametersPerBody));
    }

    public int Result { get; }

    public WarpIrValueType ResultType { get; }

    public WarpIrOpCode OpCode { get; }

    public int Left { get; }

    public int Right { get; }

    public uint Immediate { get; }

    public int Third { get; }

    public int Callee { get; }

    public IReadOnlyList<int> Arguments => arguments is null
        ? Array.Empty<int>()
        : arguments;
}
