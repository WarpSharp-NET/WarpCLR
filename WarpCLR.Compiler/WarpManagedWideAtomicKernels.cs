using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static class WarpManagedWideAtomicKernels
{
    internal static IReadOnlyList<WarpLogicalMachineLayout> Create64()
    {
        (string Name, WarpIrOpCode Code)[] operations =
        [
            ("load-sc", WarpManagedWideAtomicOpCode.LoadSequential),
            ("store-sc", WarpManagedWideAtomicOpCode.StoreSequential),
            ("compare-exchange", WarpManagedWideAtomicOpCode.CompareExchange),
            ("exchange", WarpManagedWideAtomicOpCode.Exchange),
            ("add", WarpManagedWideAtomicOpCode.Add),
            ("increment", WarpManagedWideAtomicOpCode.Increment),
            ("decrement", WarpManagedWideAtomicOpCode.Decrement),
            ("and", WarpManagedWideAtomicOpCode.And),
            ("or", WarpManagedWideAtomicOpCode.Or),
            ("load-acquire", WarpManagedWideAtomicOpCode.LoadAcquire),
            ("store-release", WarpManagedWideAtomicOpCode.StoreRelease),
        ];
        return operations.Select(operation => Create(operation.Name, operation.Code)).ToArray();
    }

    private static WarpLogicalMachineLayout Create(string name, WarpIrOpCode code)
    {
        int arity = WarpManagedWideAtomicOpCode.OperandWords(code) + 1;
        var instructions = new List<WarpIrInstruction>();
        for (int index = 0; index < arity; index++)
        {
            instructions.Add(new WarpIrInstruction(index, WarpIrOpCode.LoadInput, immediate: (uint)index));
        }
        instructions.Add(new WarpIrInstruction(arity, code, 0, Enumerable.Range(1, arity - 1), 2));
        var kernel = new WarpControlFlowKernel(WarpManagedWideAtomicOpCode.Semantics + "/" + name, arity, 0,
            [new WarpBasicBlock(0, [], instructions, new WarpTupleReturnTerminator([arity, arity + 1]))]);
        return new(kernel);
    }
}
