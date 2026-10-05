using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static class WarpExceptionMachineHookKernels
{
    internal static IReadOnlyList<WarpLogicalMachineLayout> Create() =>
        [CreateUnwindTransfer(), CreateTerminalTransfer(), CreateGuardTransfer(0, 0),
            CreateGuardTransfer(uint.MaxValue, 0), CreateGuardTransfer(1, 0), CreateGuardTransfer(1, uint.MaxValue)];

    internal static WarpLogicalMachineLayout CreateGuardTransfer(uint newDepth, uint newPc)
    {
        var kernel = new WarpControlFlowKernel($"portable.eh-hooks.guard-{newDepth}-{newPc}", 1, 0,
            [new WarpBasicBlock(0, [],
                [new WarpIrInstruction(0, WarpIrOpCode.Constant, immediate: WarpLogicalMachineLayout.DepthOffset),
                    new WarpIrInstruction(1, WarpIrOpCode.Constant, immediate: newDepth),
                    new WarpIrInstruction(2, WarpManagedStateOpCode.StoreWord, 0, 1),
                    new WarpIrInstruction(3, WarpIrOpCode.Constant, immediate: WarpLogicalMachineLayout.HeaderWords + WarpLogicalMachineLayout.FrameProgramCounterOffset),
                    new WarpIrInstruction(4, WarpIrOpCode.Constant, immediate: newPc),
                    new WarpIrInstruction(5, WarpManagedStateOpCode.StoreWord, 3, 4)],
                new WarpStateDispatchTerminator([new(0, 1)])),
                new WarpBasicBlock(1, [], [new WarpIrInstruction(6, WarpIrOpCode.Constant, immediate: 77)], new WarpReturnTerminator(6))],
            reduction: null, functions: null,
            new WarpLogicalExecutionMetadata([new WarpLogicalBodyMetadata(0, runtimeHelper: true, [0, 0])],
                frameOwners: true, runtimeStateAccess: true, nonlocalStateDispatch: true));
        return new WarpLogicalMachineLayout(kernel);
    }

    internal static WarpLogicalMachineLayout CreateUnwindTransfer()
    {
        var prototype = new WarpLogicalMachineLayout(CreateTransferKernel(0));
        return new WarpLogicalMachineLayout(CreateTransferKernel(checked((uint)prototype.GetBlockEntry(0, 2))));
    }

    private static WarpControlFlowKernel CreateTransferKernel(uint handlerPc)
    {
        var helper = new WarpControlFlowFunction(0, "apply-nonlocal-handler", 0,
            [new WarpBasicBlock(0, [],
                [new WarpIrInstruction(0, WarpIrOpCode.Constant, immediate: WarpLogicalMachineLayout.DepthOffset),
                    new WarpIrInstruction(1, WarpIrOpCode.Constant, immediate: 1),
                    new WarpIrInstruction(2, WarpManagedStateOpCode.StoreWord, 0, 1),
                    new WarpIrInstruction(3, WarpIrOpCode.Constant, immediate: WarpLogicalMachineLayout.HeaderWords + WarpLogicalMachineLayout.FrameProgramCounterOffset),
                    new WarpIrInstruction(4, WarpIrOpCode.Constant, immediate: handlerPc),
                    new WarpIrInstruction(5, WarpManagedStateOpCode.StoreWord, 3, 4)],
                new WarpStateDispatchTerminator([new(0, 2)]))]);
        return new WarpControlFlowKernel("portable.eh-hooks.nonlocal-handler", 1, 0,
            [new WarpBasicBlock(0, [], [new WarpIrInstruction(0, WarpIrOpCode.LoadInput),
                new WarpIrInstruction(1, WarpIrOpCode.Call, callee: 0)], new WarpBranchTerminator(new WarpBranchTarget(1, []))),
            new WarpBasicBlock(1, [], [new WarpIrInstruction(2, WarpIrOpCode.Constant, immediate: 0xDEADBEEF)], new WarpReturnTerminator(2)),
            new WarpBasicBlock(2, [], [new WarpIrInstruction(3, WarpIrOpCode.Constant, immediate: 99)], new WarpReturnTerminator(3))],
            reduction: null, [helper], new WarpLogicalExecutionMetadata([
                new WarpLogicalBodyMetadata(1, runtimeHelper: false, [1, 1, 1]),
                new WarpLogicalBodyMetadata(0, runtimeHelper: true, [0])],
                frameOwners: true, runtimeStateAccess: true, nonlocalStateDispatch: true));
    }

    internal static WarpLogicalMachineLayout CreateTerminalTransfer()
    {
        var kernel = new WarpControlFlowKernel("portable.eh-hooks.terminal", 1, 0,
            [new WarpBasicBlock(0, [],
                [new WarpIrInstruction(0, WarpIrOpCode.Constant, immediate: WarpLogicalMachineLayout.ResultOffset),
                    new WarpIrInstruction(1, WarpIrOpCode.Constant, immediate: 0x7FA12345),
                    new WarpIrInstruction(2, WarpManagedStateOpCode.StoreWord, 0, 1),
                    new WarpIrInstruction(3, WarpIrOpCode.Constant, immediate: WarpLogicalMachineLayout.StatusOffset),
                    new WarpIrInstruction(4, WarpIrOpCode.Constant, immediate: WarpLogicalMachineLayout.Completed),
                    new WarpIrInstruction(5, WarpManagedStateOpCode.StoreWord, 3, 4)],
                new WarpStateDispatchTerminator([new(0, 0)]))], reduction: null, functions: null,
            new WarpLogicalExecutionMetadata([new WarpLogicalBodyMetadata(0, runtimeHelper: true, [0])],
                frameOwners: true, runtimeStateAccess: true, nonlocalStateDispatch: true));
        return new WarpLogicalMachineLayout(kernel);
    }
}
