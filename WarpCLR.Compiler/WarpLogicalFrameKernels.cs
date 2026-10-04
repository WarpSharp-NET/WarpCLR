using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static class WarpLogicalFrameKernels
{
    internal static IReadOnlyList<WarpLogicalMachineLayout> Create() =>
        [CreateRecursiveSum(), CreateHelperLoop(), CreatePrivateReset(), CreateTuple()];

    internal static WarpLogicalMachineLayout CreateRecursiveSum()
    {
        var function = new WarpControlFlowFunction(0, "source.sum", 1,
        [
            new WarpBasicBlock(0, [],
            [new WarpIrInstruction(0, WarpIrOpCode.LoadArgument),
                new WarpIrInstruction(1, WarpManagedFrameOpCode.StorePrivateWord, 0),
                new WarpIrInstruction(2, WarpIrOpCode.Constant), new WarpIrInstruction(3, WarpIrOpCode.Equal, 0, 2)],
                new WarpConditionalBranchTerminator(3, new WarpBranchTarget(1, []), new WarpBranchTarget(2, []))),
            new WarpBasicBlock(1, [], [new WarpIrInstruction(4, WarpIrOpCode.Constant)], new WarpReturnTerminator(4)),
            new WarpBasicBlock(2, [],
            [new WarpIrInstruction(5, WarpManagedFrameOpCode.LoadPrivateWord),
                new WarpIrInstruction(6, WarpIrOpCode.Constant, immediate: 1), new WarpIrInstruction(7, WarpIrOpCode.Subtract, 5, 6),
                new WarpIrInstruction(8, WarpIrOpCode.Call, callee: 0, arguments: [7]),
                new WarpIrInstruction(9, WarpManagedFrameOpCode.LoadPrivateWord), new WarpIrInstruction(10, WarpIrOpCode.Add, 8, 9)],
                new WarpReturnTerminator(10)),
        ]);
        return Entry("recursive-sum", [function], [new(0, true, [0]), new(1, false, [1, 1, 1])]);
    }

    internal static WarpLogicalMachineLayout CreateHelperLoop()
    {
        var source = new WarpControlFlowFunction(0, "source.helper-loop", 1,
            [new WarpBasicBlock(0, [], [new WarpIrInstruction(0, WarpIrOpCode.LoadArgument),
                new WarpIrInstruction(1, WarpIrOpCode.Call, callee: 1, arguments: [0])], new WarpReturnTerminator(1))]);
        var helper = new WarpControlFlowFunction(1, "runtime.helper.outer", 1,
            [new WarpBasicBlock(0, [], [new WarpIrInstruction(0, WarpIrOpCode.LoadArgument),
                new WarpIrInstruction(1, WarpIrOpCode.Call, callee: 2, arguments: [0])], new WarpReturnTerminator(1))]);
        var loop = new WarpControlFlowFunction(2, "runtime.helper.loop", 1,
        [
            new WarpBasicBlock(0, [], [new WarpIrInstruction(0, WarpIrOpCode.LoadArgument), new WarpIrInstruction(1, WarpIrOpCode.Constant)],
                new WarpBranchTerminator(new WarpBranchTarget(1, [0, 1]))),
            new WarpBasicBlock(1, [new WarpBlockParameter(2), new WarpBlockParameter(3)],
                [new WarpIrInstruction(4, WarpIrOpCode.Constant), new WarpIrInstruction(5, WarpIrOpCode.Equal, 2, 4)],
                new WarpConditionalBranchTerminator(5, new WarpBranchTarget(3, [3]), new WarpBranchTarget(2, [2, 3]))),
            new WarpBasicBlock(2, [new WarpBlockParameter(6), new WarpBlockParameter(7)],
                [new WarpIrInstruction(8, WarpIrOpCode.Constant, immediate: 1), new WarpIrInstruction(9, WarpIrOpCode.Subtract, 6, 8),
                    new WarpIrInstruction(10, WarpIrOpCode.Add, 7, 8)], new WarpBranchTerminator(new WarpBranchTarget(1, [9, 10]))),
            new WarpBasicBlock(3, [new WarpBlockParameter(11)], [], new WarpReturnTerminator(11)),
        ]);
        return Entry("helper-loop", [source, helper, loop],
            [new(0, true, [0]), new(0, false, [1]), new(0, true, [0]), new(0, true, [0, 0, 0, 0])]);
    }

    internal static WarpLogicalMachineLayout CreatePrivateReset()
    {
        var function = new WarpControlFlowFunction(0, "source.initial-private", 0,
            [new WarpBasicBlock(0, [], [new WarpIrInstruction(0, WarpManagedFrameOpCode.LoadPrivateWord),
                new WarpIrInstruction(1, WarpIrOpCode.Constant, immediate: 0xDEADBEEF),
                new WarpIrInstruction(2, WarpManagedFrameOpCode.StorePrivateWord, 1)], new WarpReturnTerminator(0))]);
        var kernel = new WarpControlFlowKernel("portable.frames.private-reset", 1, 0,
            [new WarpBasicBlock(0, [], [new WarpIrInstruction(0, WarpIrOpCode.Call, callee: 0),
                new WarpIrInstruction(1, WarpIrOpCode.Call, callee: 0), new WarpIrInstruction(2, WarpIrOpCode.Add, 0, 1)],
                new WarpReturnTerminator(2))], null, [function],
            new WarpLogicalExecutionMetadata([new(1, false, [1]), new(1, false, [1])]));
        return new WarpLogicalMachineLayout(kernel);
    }

    internal static WarpLogicalMachineLayout CreateTuple()
    {
        WarpControlFlowKernel original = WarpPortableResultKernels.CreateTupleCall().Kernel;
        var kernel = new WarpControlFlowKernel("portable.frames.tuple-tail", original.InputBufferCount, 0,
            original.Blocks, null, original.Functions, new WarpLogicalExecutionMetadata([new(0, true, [0]), new(0, false, [1])]));
        return new WarpLogicalMachineLayout(kernel);
    }

    private static WarpLogicalMachineLayout Entry(string name, WarpControlFlowFunction[] functions, WarpLogicalBodyMetadata[] bodies)
    {
        var kernel = new WarpControlFlowKernel("portable.frames." + name, 1, 0,
            [new WarpBasicBlock(0, [], [new WarpIrInstruction(0, WarpIrOpCode.LoadInput),
                new WarpIrInstruction(1, WarpIrOpCode.Call, callee: 0, arguments: [0])], new WarpReturnTerminator(1))],
            null, functions, new WarpLogicalExecutionMetadata(bodies));
        return new WarpLogicalMachineLayout(kernel);
    }
}
