using System.Collections.Frozen;
using System.Collections.ObjectModel;

namespace WarpCLR.IR;

public sealed class WarpLogicalMachineLayout
{
    public const string Version = "warp.logical-machine/0.4";
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
    internal const int ResultHighOffset = 8;
    internal const int LogicalDepthOffset = 9;
    internal const int UsedOperationsLowOffset = 10;
    internal const int UsedOperationsHighOffset = 11;
    internal const uint OperationalOverflowFault = 5;
    public const int FrameFunctionOffset = 0;
    public const int FrameProgramCounterOffset = 1;
    public const int FrameReturnValueOffset = 2;
    internal const int FrameReturnWordCountOffset = 3;
    public const uint Runnable = 0;
    public const uint Completed = 1;
    public const uint Faulted = 2;
    public const uint StepLimitFault = 1;
    public const uint CallDepthFault = 2;
    internal const uint ManagedMemoryBoundsFault = 4;

    private readonly FrozenDictionary<(int Function, int Block), int> blockEntries;

    public WarpLogicalMachineLayout(WarpControlFlowKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        WarpCompilationAdmission.Validate(kernel);
        Kernel = kernel;
        RequiresManagedMemory = kernel.Instructions.Concat(kernel.Functions.SelectMany(function => function.Instructions))
            .Any(instruction => WarpManagedMemoryOpCode.RequiresArena(instruction.OpCode));
        ResultWordCount = kernel.Blocks.Select(block => block.Terminator).OfType<WarpTupleReturnTerminator>()
            .Select(tuple => tuple.Values.Count).DefaultIfEmpty(1).First();
        MaximumValueCount = Math.Max(kernel.ValueCount, kernel.Functions.Count == 0 ? 0 : kernel.Functions.Max(function => function.ValueCount));
        MaximumArgumentCount = kernel.Functions.Count == 0 ? 0 : kernel.Functions.Max(function => function.ParameterCount);
        ArgumentOffset = checked(FrameHeaderWords + MaximumValueCount);
        PrivateOffset = checked(ArgumentOffset + MaximumArgumentCount);
        MaximumPrivateWords = kernel.Execution?.Bodies.Max(body => body.PrivateWordCount) ?? 0;
        FrameWords = checked((PrivateOffset + MaximumPrivateWords + 15) / 16 * 16);
        var entries = new Dictionary<(int Function, int Block), int>();
        var nodes = new List<WarpLogicalMachineNode>();
        AppendBody(0, kernel.Blocks, entries, nodes, kernel.Execution?.Bodies[0]);
        foreach (WarpControlFlowFunction function in kernel.Functions)
        {
            AppendBody(function.Id + 1, function.Blocks, entries, nodes, kernel.Execution?.Bodies[function.Id + 1]);
        }

        blockEntries = entries.ToFrozenDictionary();
        Nodes = nodes.AsReadOnly();
        MaximumBlockCost = Nodes.Max(node => node.BlockCost);
    }

    internal int PrivateOffset { get; }

    internal int MaximumPrivateWords { get; }

    internal bool HasLogicalAccounting => Kernel.Execution is not null;

    internal bool IsRuntimeHelper(int function) => Kernel.Execution?.Bodies[function].RuntimeHelper == true;

    internal int GetPhysicalFrameCapacity(int logicalCallDepth) => checked(logicalCallDepth * Kernel.HelperExpansionFactor +
        (Kernel.Execution?.Bodies[0].RuntimeHelper == true ? 1 : 0));

    internal int GetPrivateWordCount(int function) => Kernel.Execution?.Bodies[function].PrivateWordCount ?? 0;

    public WarpControlFlowKernel Kernel { get; }

    public int MaximumValueCount { get; }

    public int MaximumArgumentCount { get; }

    public int ArgumentOffset { get; }

    public int FrameWords { get; }

    public int MaximumBlockCost { get; }

    public ReadOnlyCollection<WarpLogicalMachineNode> Nodes { get; }

    internal int ResultWordCount { get; }

    internal bool RequiresManagedMemory { get; }

    internal int ResultTailWords => Math.Max(0, ResultWordCount - 2);

    internal int GetResultWordOffset(int word, int maximumCallDepth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(word);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(word, ResultWordCount);
        return word < 2 ? ResultOffset + word : checked(HeaderWords + FrameWords * GetPhysicalFrameCapacity(maximumCallDepth) + word - 2);
    }

    public int GetBlockEntry(int function, int block) => blockEntries[(function, block)];

    public int GetStateWords(int maximumCallDepth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCallDepth);
        return checked(HeaderWords + FrameWords * GetPhysicalFrameCapacity(maximumCallDepth) + ResultTailWords);
    }

    public uint[] CreateInitialState(int maximumCallDepth, long maximumSteps)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSteps);
        uint[] state = new uint[GetStateWords(maximumCallDepth)];
        ResetState(state, maximumSteps);
        return state;
    }

    public void ResetState(Span<uint> state, long maximumSteps)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSteps);
        ArgumentOutOfRangeException.ThrowIfLessThan(state.Length, HeaderWords + FrameWords + ResultTailWords, nameof(state));
        state[..HeaderWords].Clear();
        state.Slice(HeaderWords, FrameHeaderWords).Clear();
        if (ResultTailWords != 0)
        {
            state[^ResultTailWords..].Clear();
        }
        state[RemainingStepsLowOffset] = unchecked((uint)maximumSteps);
        state[RemainingStepsHighOffset] = (uint)((ulong)maximumSteps >> 32);
        state[DepthOffset] = 1;
        if (HasLogicalAccounting)
        {
            state[LogicalDepthOffset] = IsRuntimeHelper(0) ? 0u : 1u;
        }
        if (MaximumPrivateWords != 0)
        {
            state.Slice(HeaderWords + PrivateOffset, MaximumPrivateWords).Clear();
        }
        state[HeaderWords + FrameProgramCounterOffset] = checked((uint)GetBlockEntry(0, 0));
    }

    private static void AppendBody(int function, IReadOnlyList<WarpBasicBlock> blocks, Dictionary<(int Function, int Block), int> entries, List<WarpLogicalMachineNode> nodes, WarpLogicalBodyMetadata? metadata)
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
                    nodes.Add(new WarpLogicalMachineNode(pc, function, block, startsBlock, instructions, instruction, pc + 1, metadata?.SourceBlockCosts[block.Id]));
                    instructions.Clear();
                    startsBlock = false;
                }
                else
                {
                    instructions.Add(instruction);
                }
            }

            nodes.Add(new WarpLogicalMachineNode(nodes.Count, function, block, startsBlock, instructions, null, -1, metadata?.SourceBlockCosts[block.Id]));
        }
    }
}
