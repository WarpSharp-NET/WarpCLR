using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static class WarpLogicalWorkerHookKernels
{
    internal static WarpLogicalMachineLayout CreateDirect()
    {
        WarpBasicBlock[] blocks = [new(0, [], [Worker(0)], new WarpReturnTerminator(0))];
        var metadata = new WarpLogicalExecutionMetadata([new(0, false, [1])], logicalWorkerAccess: true);
        return new(new WarpControlFlowKernel("logical-worker-direct", 0, 0, blocks, null, [], metadata));
    }

    internal static WarpLogicalMachineLayout CreateNested()
    {
        WarpBasicBlock[] entry = [new(0, [], [Worker(0), Call(1, 0)], new WarpTupleReturnTerminator([0, 1]))];
        WarpBasicBlock[] source = [new(0, [], [Worker(0), Call(1, 1), new(2, WarpIrOpCode.ExclusiveOr, 0, 1)], new WarpReturnTerminator(2))];
        WarpBasicBlock[] helper = [new(0, [], [Worker(0), new(1, WarpIrOpCode.Constant, immediate: 0x80000000),
            new(2, WarpIrOpCode.ExclusiveOr, 0, 1)], new WarpReturnTerminator(2))];
        WarpControlFlowFunction[] functions = [new(0, "original-source", 0, source), new(1, "trusted-helper", 0, helper)];
        var metadata = new WarpLogicalExecutionMetadata([new(0, false, [1]), new(0, false, [1]), new(0, true, [0])],
            frameOwners: true, logicalWorkerAccess: true);
        return new(new WarpControlFlowKernel("logical-worker-nested", 0, 0, entry, null, functions, metadata));
    }

    internal static WarpLogicalMachineLayout CreateInputBinding()
    {
        WarpBasicBlock[] blocks = [new(0, [], [Worker(0), new(1, WarpIrOpCode.LoadInput),
            new(2, WarpIrOpCode.ExclusiveOr, 0, 1)], new WarpTupleReturnTerminator([0, 1, 2]))];
        var metadata = new WarpLogicalExecutionMetadata([new(0, false, [1])], logicalWorkerAccess: true);
        return new(new WarpControlFlowKernel("logical-worker-input-binding", 1, 0, blocks, null, [], metadata));
    }

    private static WarpIrInstruction Worker(int result) => new(result, WarpManagedInvocationOpCode.LoadLogicalWorker);

    private static WarpIrInstruction Call(int result, int callee) => new(result, WarpIrOpCode.Call, callee: callee, arguments: []);
}
