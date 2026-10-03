using System.Collections.Frozen;
using System.Collections.ObjectModel;

namespace WarpCLR.IR;

public sealed class WarpLogicalMachineLayout
{
    public const string Version = "warp.logical-machine/0.1";
    public const int HeaderWords = 16;
    public const int FrameHeaderWords = 4;
    public const int StatusOffset = 0;
    public const int FaultKindOffset = 1;
    public const int FaultFunctionOffset = 2;
    public const int FaultBlockOffset = 3;
    public const int RemainingStepsLowOffset = 4;
    public const int RemainingStepsHighOffset = 5;
    public const int DepthOffset = 6;
    public const int ResultOffset = 7;
    public const int FrameFunctionOffset = 0;
    public const int FrameProgramCounterOffset = 1;
    public const int FrameReturnValueOffset = 2;
    public const uint Runnable = 0;
    public const uint Completed = 1;
    public const uint Faulted = 2;
    public const uint StepLimitFault = 1;
    public const uint CallDepthFault = 2;

    private readonly FrozenDictionary<(int Function, int Block), int> blockEntries;

    public WarpLogicalMachineLayout(WarpControlFlowKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        Kernel = kernel;
        MaximumValueCount = Math.Max(kernel.ValueCount, kernel.Functions.Count == 0 ? 0 : kernel.Functions.Max(function => function.ValueCount));
        MaximumArgumentCount = kernel.Functions.Count == 0 ? 0 : kernel.Functions.Max(function => function.ParameterCount);
        ArgumentOffset = checked(FrameHeaderWords + MaximumValueCount);
        FrameWords = checked((ArgumentOffset + MaximumArgumentCount + 15) / 16 * 16);
        var entries = new Dictionary<(int Function, int Block), int>();
        var nodes = new List<WarpLogicalMachineNode>();
        AppendBody(0, kernel.Blocks, entries, nodes);
        foreach (WarpControlFlowFunction function in kernel.Functions)
        {
            AppendBody(function.Id + 1, function.Blocks, entries, nodes);
        }

        blockEntries = entries.ToFrozenDictionary();
        Nodes = nodes.AsReadOnly();
        MaximumBlockCost = Nodes.Max(node => node.BlockCost);
    }

    public WarpControlFlowKernel Kernel { get; }

    public int MaximumValueCount { get; }

    public int MaximumArgumentCount { get; }

    public int ArgumentOffset { get; }

    public int FrameWords { get; }

    public int MaximumBlockCost { get; }

    public ReadOnlyCollection<WarpLogicalMachineNode> Nodes { get; }

    public int GetBlockEntry(int function, int block) => blockEntries[(function, block)];

    public int GetStateWords(int maximumCallDepth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCallDepth);
        return checked(HeaderWords + FrameWords * maximumCallDepth);
    }

    public uint[] CreateInitialState(int maximumCallDepth, long maximumSteps)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSteps);
        uint[] state = new uint[GetStateWords(maximumCallDepth)];
        state[RemainingStepsLowOffset] = unchecked((uint)maximumSteps);
        state[RemainingStepsHighOffset] = (uint)((ulong)maximumSteps >> 32);
        state[DepthOffset] = 1;
        state[HeaderWords + FrameProgramCounterOffset] = checked((uint)GetBlockEntry(0, 0));
        return state;
    }

    private static void AppendBody(int function, IReadOnlyList<WarpBasicBlock> blocks, Dictionary<(int Function, int Block), int> entries, List<WarpLogicalMachineNode> nodes)
    {
        foreach (WarpBasicBlock block in blocks)
        {
            entries.Add((function, block.Id), nodes.Count);
            var instructions = new List<WarpIrInstruction>();
            bool startsBlock = true;
            foreach (WarpIrInstruction instruction in block.Instructions)
            {
                if (instruction.OpCode == WarpIrOpCode.Call)
                {
                    int pc = nodes.Count;
                    nodes.Add(new WarpLogicalMachineNode(pc, function, block, startsBlock, instructions, instruction, pc + 1));
                    instructions.Clear();
                    startsBlock = false;
                }
                else
                {
                    instructions.Add(instruction);
                }
            }

            nodes.Add(new WarpLogicalMachineNode(nodes.Count, function, block, startsBlock, instructions, null, -1));
        }
    }
}
