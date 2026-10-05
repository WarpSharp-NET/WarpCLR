using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static class WarpManagedExceptionHookKernels
{
    internal static WarpLogicalMachineLayout Create(int resultWordCount = 1, int sourceCost = 1)
    {
        return new(new WarpControlFlowKernel($"portable.managed-exception.words-{resultWordCount}-cost-{sourceCost}", 3, 0,
            [new(0, [], [new(0, WarpIrOpCode.LoadInput, immediate: 0), new(1, WarpIrOpCode.LoadInput, immediate: 1),
                new(2, WarpIrOpCode.LoadInput, immediate: 2)], new WarpManagedExceptionTerminator(0, 1, 2, resultWordCount))],
            reduction: null, functions: null, new WarpLogicalExecutionMetadata([new(0, false, [sourceCost])],
                frameOwners: true, runtimeStateAccess: true, nonlocalStateDispatch: true, managedExceptionTermination: true)));
    }

    internal static WarpLogicalMachineLayout CreateHelper()
    {
        var helper = new WarpControlFlowFunction(0, "terminate-escaped-object", 3,
            [new(0, [], [new(0, WarpIrOpCode.LoadArgument, immediate: 0), new(1, WarpIrOpCode.LoadArgument, immediate: 1),
                new(2, WarpIrOpCode.LoadArgument, immediate: 2)], new WarpManagedExceptionTerminator(0, 1, 2))]);
        return new(new WarpControlFlowKernel("portable.managed-exception.helper", 3, 0,
            [new(0, [], [new(0, WarpIrOpCode.LoadInput, immediate: 0), new(1, WarpIrOpCode.LoadInput, immediate: 1),
                new(2, WarpIrOpCode.LoadInput, immediate: 2), new(3, WarpIrOpCode.Call, callee: 0, arguments: [0, 1, 2])],
                new WarpBranchTerminator(new(1, []))), new(1, [], [new(4, WarpIrOpCode.Constant, immediate: 0xDEADBEEF)], new WarpReturnTerminator(4))],
            reduction: null, [helper], new WarpLogicalExecutionMetadata([new(0, false, [1, 1]), new(0, true, [0])],
                frameOwners: true, runtimeStateAccess: true, nonlocalStateDispatch: true, managedExceptionTermination: true)));
    }
}
