using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static class WarpManagedMemoryKernels
{
    internal static WarpLogicalMachineLayout CreateWriteReadCount()
    {
        var helper = new WarpControlFlowFunction(0, "managed.word.store-and-load", 2,
            [new WarpBasicBlock(0, [],
                [new WarpIrInstruction(0, WarpIrOpCode.LoadArgument),
                    new WarpIrInstruction(1, WarpIrOpCode.LoadArgument, immediate: 1),
                    new WarpIrInstruction(2, WarpManagedMemoryOpCode.StoreWord, 0, 1),
                    new WarpIrInstruction(3, WarpManagedMemoryOpCode.LoadWord, left: 0)],
                new WarpReturnTerminator(3))]);
        var kernel = new WarpControlFlowKernel("portable.managed-word.store-load-count/0.1", 2, 0,
            [new WarpBasicBlock(0, [],
                [new WarpIrInstruction(0, WarpIrOpCode.LoadInput),
                    new WarpIrInstruction(1, WarpIrOpCode.LoadInput, immediate: 1),
                    new WarpIrInstruction(2, WarpIrOpCode.Call, callee: 0, arguments: [0, 1]),
                    new WarpIrInstruction(3, WarpManagedMemoryOpCode.WordCount)],
                new WarpWideReturnTerminator(2, 3))], functions: [helper]);
        return new WarpLogicalMachineLayout(kernel);
    }
}
