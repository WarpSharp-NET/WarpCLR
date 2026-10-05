using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static class WarpFilterAliasHookKernels
{
    internal const uint EvaluationSentinel = 0x13579BDF;
    internal const uint PrefixSentinel = 0xBAD00BAD;
    internal const uint OriginalTail = 0xCAFEBABE;
    internal const int OwnerDepthResult = 20;
    internal const int OwnerActivationResult = 21;
    internal const int OwnerContextResult = 22;
    internal const int PrefixShadowResult = 23;
    internal const int EvaluationResult = 24;

    internal static WarpLogicalMachineLayout Create(bool callSource = false, uint ownerDepth = 1, uint ownerActivation = 1)
    {
        WarpLogicalMachineLayout prototype = new(CreateKernel(0, 0, 0, 0, callSource, ownerDepth, ownerActivation));
        return new(CreateKernel(prototype.FrameWords, prototype.PrivateOffset,
            checked((uint)prototype.GetBlockEntry(2, 0)), checked((uint)prototype.GetBlockEntry(0, 2)),
            callSource, ownerDepth, ownerActivation));
    }

    private static WarpControlFlowKernel CreateKernel(int frameWords, int privateOffset, uint filterPc,
        uint handlerPc, bool callSource, uint ownerDepth, uint ownerActivation)
    {
        var functions = new List<WarpControlFlowFunction> { CreateEnter(frameWords, privateOffset, filterPc, ownerDepth, ownerActivation),
            CreateFilter(callSource), CreateFinish(frameWords, privateOffset, handlerPc) };
        var metadata = new List<WarpLogicalBodyMetadata>
        {
            new(2, false, [1, 1, 1]), new(0, true, [0]),
            new(2, false, [1], countsSourceDepth: false, aliasOwnerFunction: 0, aliasPrefixWords: 1), new(0, true, [0]),
        };
        if (callSource) { functions.Add(CreateCalledSource()); metadata.Add(new(0, false, [1])); }
        WarpBasicBlock[] root = [new(0, [], [new(0, WarpIrOpCode.LoadInput),
            new(1, WarpManagedFrameOpCode.StorePrivateWord, 0), new(2, WarpIrOpCode.Constant, immediate: OriginalTail),
            new(3, WarpManagedFrameOpCode.StorePrivateWord, 2, immediate: 1), new(4, WarpIrOpCode.Call, callee: 0)],
            new WarpBranchTerminator(new(1, []))),
            new(1, [], [new(5, WarpIrOpCode.Constant, immediate: 0xDEADBEEF)], new WarpTupleReturnTerminator([5, 5])),
            new(2, [], [new(6, WarpManagedFrameOpCode.LoadPrivateWord),
                new(7, WarpManagedFrameOpCode.LoadPrivateWord, immediate: 1)], new WarpTupleReturnTerminator([6, 7]))];
        return new("portable.filter-alias." + (callSource ? "called-source" : $"owner-{ownerDepth}-{ownerActivation}"), 1, 0,
            root, reduction: null, functions, new WarpLogicalExecutionMetadata(metadata,
                frameOwners: true, runtimeStateAccess: true, nonlocalStateDispatch: true));
    }

    private static WarpControlFlowFunction CreateEnter(int frameWords, int privateOffset, uint filterPc, uint ownerDepth, uint ownerActivation)
    {
        var code = new List<WarpIrInstruction>();
        int frame = checked(WarpLogicalMachineLayout.HeaderWords + 2 * frameWords);
        Write(code, frame + WarpLogicalMachineLayout.FrameFunctionOffset, 2);
        Write(code, frame + WarpLogicalMachineLayout.FrameProgramCounterOffset, filterPc);
        Write(code, frame + WarpLogicalMachineLayout.FrameReturnValueOffset, 0);
        Write(code, frame + WarpLogicalMachineLayout.FrameReturnWordCountOffset, 0);
        Write(code, frame + WarpLogicalMachineLayout.FrameActivationOffset, 3);
        Write(code, frame + WarpLogicalMachineLayout.FramePrivateWordsOffset, 2);
        Write(code, frame + WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset, ownerDepth);
        Write(code, frame + WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset, ownerActivation);
        Write(code, frame + privateOffset, PrefixSentinel);
        Write(code, frame + privateOffset + 1, 0);
        Write(code, WarpLogicalMachineLayout.NextActivationOffset, 3);
        Write(code, WarpLogicalMachineLayout.DepthOffset, 3);
        return new(0, "enter-filter", 0, [new(0, [], code, new WarpStateDispatchTerminator([new(2, 0)]))]);
    }

    private static WarpControlFlowFunction CreateFilter(bool callSource)
    {
        var code = new List<WarpIrInstruction> { new(0, WarpManagedFrameOpCode.LoadPrivateWord),
            new(1, WarpIrOpCode.Constant, immediate: 1), new(2, WarpIrOpCode.Add, 0, 1),
            new(3, WarpManagedFrameOpCode.StorePrivateWord, 2), new(4, WarpIrOpCode.Constant, immediate: EvaluationSentinel),
            new(5, WarpManagedFrameOpCode.StorePrivateWord, 4, immediate: 1),
            new(6, WarpManagedFrameOpCode.OwnerFrame), new(7, WarpManagedFrameOpCode.OwnerGeneration),
            new(8, WarpManagedFrameOpCode.OwnerContext) };
        WriteValue(code, OwnerDepthResult, 6);
        WriteValue(code, OwnerActivationResult, 7);
        WriteValue(code, OwnerContextResult, 8);
        if (callSource) { code.Add(new(code.Count, WarpIrOpCode.Call, callee: 3)); }
        code.Add(new(code.Count, WarpIrOpCode.Call, callee: 2));
        return new(1, "source-filter", 0, [new(0, [], code, new WarpStateDispatchTerminator([new(0, 2)]))]);
    }

    private static WarpControlFlowFunction CreateFinish(int frameWords, int privateOffset, uint handlerPc)
    {
        var code = new List<WarpIrInstruction>();
        int frame = checked(WarpLogicalMachineLayout.HeaderWords + 2 * frameWords);
        ReadInto(code, frame + privateOffset, PrefixShadowResult);
        ReadInto(code, frame + privateOffset + 1, EvaluationResult);
        Write(code, WarpLogicalMachineLayout.HeaderWords + WarpLogicalMachineLayout.FrameProgramCounterOffset, handlerPc);
        Write(code, WarpLogicalMachineLayout.DepthOffset, 1);
        return new(2, "finish-filter", 0, [new(0, [], code, new WarpStateDispatchTerminator([new(0, 2)]))]);
    }

    private static WarpControlFlowFunction CreateCalledSource() => new(3, "called-source", 0,
        [new(0, [], [new(0, WarpIrOpCode.Constant, immediate: WarpLogicalMachineLayout.LogicalDepthOffset),
            new(1, WarpManagedStateOpCode.LoadWord, 0)], new WarpReturnTerminator(1))]);

    private static void Write(List<WarpIrInstruction> code, int offset, uint value)
    {
        int address = code.Count;
        code.Add(new(address, WarpIrOpCode.Constant, immediate: checked((uint)offset)));
        int constant = code.Count;
        code.Add(new(constant, WarpIrOpCode.Constant, immediate: value));
        code.Add(new(code.Count, WarpManagedStateOpCode.StoreWord, address, constant));
    }

    private static void WriteValue(List<WarpIrInstruction> code, int offset, int value)
    {
        int address = code.Count;
        code.Add(new(address, WarpIrOpCode.Constant, immediate: checked((uint)offset)));
        code.Add(new(code.Count, WarpManagedStateOpCode.StoreWord, address, value));
    }

    private static void ReadInto(List<WarpIrInstruction> code, int offset, int destination)
    {
        int address = code.Count;
        code.Add(new(address, WarpIrOpCode.Constant, immediate: checked((uint)offset)));
        int value = code.Count;
        code.Add(new(value, WarpManagedStateOpCode.LoadWord, address));
        WriteValue(code, destination, value);
    }
}
