using System.Collections.Frozen;
using System.Collections.ObjectModel;

namespace WarpCLR.IR;

public sealed class WarpLogicalMachineLayout
{
    public const string Version = "warp.logical-machine/0.8";
    public const int HeaderWords = 64;
    public const int FrameHeaderWords = 8;
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
    internal const int OwnerContextOffset = 12;
    internal const int NextActivationOffset = 13;
    internal const int SourceBoundaryModeOffset = 14;
    internal const int SourceBoundaryStateOffset = 15;
    internal const int FrameStrideOffset = 16;
    internal const int PrivateBaseOffset = 17;
    internal const int InteriorFaultOffset = 18;
    internal const int InteriorResultOffset = 19;
    internal const uint BeforeSourceBoundary = 1;
    internal const uint AcknowledgedSourceBoundary = 2;
    internal const uint OperationalOverflowFault = 5;
    internal const uint ActivationExhaustionFault = 6;
    internal const uint PhysicalFrameCapacityFault = 7;
    internal const uint ManagedExceptionFault = 8;
    internal const int EscapedExceptionContextOffset = 25;
    internal const int EscapedExceptionObjectOffset = 26;
    internal const int EscapedExceptionGenerationOffset = 27;
    public const int FrameFunctionOffset = 0;
    public const int FrameProgramCounterOffset = 1;
    public const int FrameReturnValueOffset = 2;
    internal const int FrameReturnWordCountOffset = 3;
    internal const int FrameActivationOffset = 4;
    internal const int FramePrivateWordsOffset = 5;
    internal const int FrameAliasOwnerDepthOffset = 6;
    internal const int FrameAliasOwnerActivationOffset = 7;
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
        RequiresManagedMemory = kernel.Execution?.ManagedExceptionTermination == true || kernel.Instructions.Concat(kernel.Functions.SelectMany(function => function.Instructions))
            .Any(instruction => WarpManagedMemoryOpCode.RequiresArena(instruction.OpCode));
        ResultWordCount = kernel.Blocks.Select(block => block.Terminator switch
        {
            WarpTupleReturnTerminator tuple => tuple.Values.Count,
            WarpReturnTerminator => 1,
            WarpStateDispatchTerminator dispatch => dispatch.ResultWordCount,
            WarpManagedExceptionTerminator managed => managed.ResultWordCount,
            _ => -1,
        }).First(words => words >= 0);
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
    internal bool HasFrameOwners => Kernel.Execution?.FrameOwners == true;
    internal bool HasManagedExceptionTermination => Kernel.Execution?.ManagedExceptionTermination == true;

    internal bool IsRuntimeHelper(int function) => Kernel.Execution?.Bodies[function].RuntimeHelper == true;
    internal bool CountsSourceDepth(int function) => Kernel.Execution?.Bodies[function].CountsSourceDepth ?? true;
    internal int GetAliasOwnerFunction(int function) => Kernel.Execution?.Bodies[function].AliasOwnerFunction ?? -1;
    internal int GetAliasPrefixWords(int function) => Kernel.Execution?.Bodies[function].AliasPrefixWords ?? 0;

    internal int GetPhysicalFrameCapacity(int logicalCallDepth) => checked(logicalCallDepth * Kernel.HelperExpansionFactor +
        (!CountsSourceDepth(0) ? 1 : 0));

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
        state[FrameStrideOffset] = checked((uint)FrameWords);
        state[PrivateBaseOffset] = checked((uint)PrivateOffset);
        state[HeaderWords + FramePrivateWordsOffset] = checked((uint)GetPrivateWordCount(0));
        if (HasFrameOwners)
        {
            state[OwnerContextOffset] = WarpLogicalOwnerNamespace.Next();
            state[NextActivationOffset] = 1;
            state[HeaderWords + FrameActivationOffset] = 1;
        }
        if (HasLogicalAccounting)
        {
            state[LogicalDepthOffset] = CountsSourceDepth(0) ? 1u : 0;
        }
        if (MaximumPrivateWords != 0)
        {
            state.Slice(HeaderWords + PrivateOffset, MaximumPrivateWords).Clear();
        }
        state[HeaderWords + FrameProgramCounterOffset] = checked((uint)GetBlockEntry(0, 0));
    }

    internal void SetSourceBoundaryMode(Span<uint> state, bool enabled)
    {
        if (!HasLogicalAccounting) { throw new InvalidOperationException("Source boundaries require admitted source metadata."); }
        ArgumentOutOfRangeException.ThrowIfLessThan(state.Length, HeaderWords + FrameWords, nameof(state));
        state[SourceBoundaryModeOffset] = enabled ? 1u : 0;
        state[SourceBoundaryStateOffset] = 0;
    }

    internal static void AcknowledgeSourceBoundary(Span<uint> state)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(state.Length, HeaderWords, nameof(state));
        if (state[SourceBoundaryModeOffset] != 1 || state[SourceBoundaryStateOffset] != BeforeSourceBoundary)
        {
            throw new InvalidOperationException("Only a published original-source boundary may be acknowledged.");
        }
        state[SourceBoundaryStateOffset] = AcknowledgedSourceBoundary;
    }

    internal bool HasValidRuntimeHeader(ReadOnlySpan<uint> state)
    {
        if (state.Length < HeaderWords || !HasValidManagedExceptionHeader(state) || state[FrameStrideOffset] != FrameWords || state[PrivateBaseOffset] != PrivateOffset ||
            state[SourceBoundaryModeOffset] > 1 || state[SourceBoundaryStateOffset] > AcknowledgedSourceBoundary ||
            state[SourceBoundaryModeOffset] == 0 && state[SourceBoundaryStateOffset] != 0 ||
            !HasLogicalAccounting && state[SourceBoundaryModeOffset] != 0)
        {
            return false;
        }
        if (HasFrameOwners ? state[OwnerContextOffset] == 0 || state[NextActivationOffset] == 0 :
            state[OwnerContextOffset] != 0 || state[NextActivationOffset] != 0)
        {
            return false;
        }
        if (state[SourceBoundaryStateOffset] == 0) { return true; }
        uint depth = state[DepthOffset];
        if (depth == 0 || depth > (uint)((state.Length - HeaderWords) / FrameWords)) { return false; }
        uint pc = state[HeaderWords + checked((int)(depth - 1) * FrameWords) + FrameProgramCounterOffset];
        return pc < Nodes.Count && Nodes[(int)pc].StartsBlock && Nodes[(int)pc].SourceCost != 0 &&
            !IsRuntimeHelper(Nodes[(int)pc].Function);
    }

    private bool HasValidManagedExceptionHeader(ReadOnlySpan<uint> state)
    {
        uint context = state[EscapedExceptionContextOffset];
        uint objectId = state[EscapedExceptionObjectOffset];
        uint generation = state[EscapedExceptionGenerationOffset];
        if (state[FaultKindOffset] == ManagedExceptionFault)
        {
            return HasManagedExceptionTermination && state[StatusOffset] == Faulted && context != 0 && objectId != 0 && generation != 0;
        }
        return context == 0 && objectId == 0 && generation == 0;
    }

    internal bool HasValidFrameIdentity(ReadOnlySpan<uint> state, int frame, int function, uint parentActivation)
    {
        if (state[frame + FramePrivateWordsOffset] != GetPrivateWordCount(function)) { return false; }
        uint activation = state[frame + FrameActivationOffset];
        if (HasFrameOwners ? activation <= parentActivation || activation > state[NextActivationOffset] : activation != 0) { return false; }
        int ownerFunction = GetAliasOwnerFunction(function);
        uint ownerDepth = state[frame + FrameAliasOwnerDepthOffset];
        uint ownerActivation = state[frame + FrameAliasOwnerActivationOffset];
        if (ownerFunction == -1) { return ownerDepth == 0 && ownerActivation == 0; }
        int depth = checked((frame - HeaderWords) / FrameWords + 1);
        if (ownerDepth == 0 || ownerDepth >= depth || ownerActivation == 0) { return false; }
        int owner = checked(HeaderWords + (int)(ownerDepth - 1) * FrameWords);
        if (state[owner + FrameFunctionOffset] != ownerFunction || state[owner + FrameActivationOffset] != ownerActivation ||
            state[owner + FramePrivateWordsOffset] != GetPrivateWordCount(ownerFunction) ||
            state[frame + FrameReturnValueOffset] != 0 || state[frame + FrameReturnWordCountOffset] != 0)
        {
            return false;
        }
        for (int previous = HeaderWords; previous < frame; previous += FrameWords)
        {
            if (state[previous + FrameAliasOwnerDepthOffset] == ownerDepth &&
                state[previous + FrameAliasOwnerActivationOffset] == ownerActivation) { return false; }
        }
        return true;
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
