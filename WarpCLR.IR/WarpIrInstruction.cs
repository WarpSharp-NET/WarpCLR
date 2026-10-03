using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace WarpCLR.IR;

public readonly record struct WarpIrInstruction
{
    private readonly ReadOnlyCollection<int>? arguments;

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
        this.arguments = Array.AsReadOnly(arguments?.ToArray() ?? []);
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
