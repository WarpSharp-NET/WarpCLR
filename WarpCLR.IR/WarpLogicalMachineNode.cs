using System.Collections.ObjectModel;

namespace WarpCLR.IR;

public sealed class WarpLogicalMachineNode
{
    internal WarpLogicalMachineNode(int programCounter, int function, WarpBasicBlock block, bool startsBlock, IEnumerable<WarpIrInstruction> instructions, WarpIrInstruction? call, int continuation, int? sourceCost = null)
    {
        ProgramCounter = programCounter;
        Function = function;
        Block = block.Id;
        StartsBlock = startsBlock;
        BlockCost = checked(block.Instructions.Count + 1);
        SourceCost = sourceCost ?? BlockCost;
        Instructions = Array.AsReadOnly(instructions.ToArray());
        Call = call;
        Continuation = continuation;
        Terminator = block.Terminator;
    }

    public int ProgramCounter { get; }

    public int Function { get; }

    public int Block { get; }

    public bool StartsBlock { get; }

    public int BlockCost { get; }

    internal int SourceCost { get; }

    public ReadOnlyCollection<WarpIrInstruction> Instructions { get; }

    public WarpIrInstruction? Call { get; }

    public int Continuation { get; }

    public WarpBlockTerminator Terminator { get; }
}
