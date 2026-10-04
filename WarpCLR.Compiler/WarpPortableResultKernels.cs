using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static class WarpPortableResultKernels
{
    public static WarpLogicalMachineLayout CreateWideCall()
    {
        var function = new WarpControlFlowFunction(0, "wide.identity", 2,
            [new WarpBasicBlock(0, [],
                [new WarpIrInstruction(0, WarpIrOpCode.LoadArgument), new WarpIrInstruction(1, WarpIrOpCode.LoadArgument, immediate: 1)],
                new WarpWideReturnTerminator(0, 1))]);
        var kernel = new WarpControlFlowKernel("portable.abi.wide-call", 2, 0,
            [new WarpBasicBlock(0, [],
                [new WarpIrInstruction(0, WarpIrOpCode.LoadInput), new WarpIrInstruction(1, WarpIrOpCode.LoadInput, immediate: 1),
                    new WarpIrInstruction(2, 0, [0, 1], 2)], new WarpWideReturnTerminator(2, 3))], functions: [function]);
        return new WarpLogicalMachineLayout(kernel);
    }

    public static WarpLogicalMachineLayout CreateVoidCall()
    {
        var function = new WarpControlFlowFunction(0, "void.helper", 0,
            [new WarpBasicBlock(0, [], [], new WarpTupleReturnTerminator([]))]);
        var kernel = new WarpControlFlowKernel("portable.abi.void-call", 1, 0,
            [new WarpBasicBlock(0, [],
                [new WarpIrInstruction(0, WarpIrOpCode.LoadInput), new WarpIrInstruction(-1, 0, [], 0)], new WarpReturnTerminator(0))], functions: [function]);
        return new WarpLogicalMachineLayout(kernel);
    }

    public static WarpLogicalMachineLayout CreateTupleCall()
    {
        var function = new WarpControlFlowFunction(0, "tuple.identity", 3,
            [new WarpBasicBlock(0, [],
                [new WarpIrInstruction(0, WarpIrOpCode.LoadArgument), new WarpIrInstruction(1, WarpIrOpCode.LoadArgument, immediate: 1),
                    new WarpIrInstruction(2, WarpIrOpCode.LoadArgument, immediate: 2)],
                new WarpTupleReturnTerminator([0, 1, 2]))]);
        var kernel = new WarpControlFlowKernel("portable.abi.tuple-call", 3, 0,
            [new WarpBasicBlock(0, [],
                [new WarpIrInstruction(0, WarpIrOpCode.LoadInput), new WarpIrInstruction(1, WarpIrOpCode.LoadInput, immediate: 1),
                    new WarpIrInstruction(2, WarpIrOpCode.LoadInput, immediate: 2), new WarpIrInstruction(3, 0, [0, 1, 2], 3)],
                new WarpTupleReturnTerminator([3, 4, 5]))], functions: [function]);
        return new WarpLogicalMachineLayout(kernel);
    }
}
