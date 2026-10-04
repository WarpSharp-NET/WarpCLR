using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static class WarpManagedAtomicKernels
{
    internal const string Semantics = "warp.managed-word.atomics32-sc-acquire-release/0.1";

    internal static IReadOnlyList<WarpLogicalMachineLayout> Create32()
    {
        (string Name, WarpIrOpCode OpCode, int Arity)[] operations =
        [
            ("load-sc", WarpManagedAtomicOpCode.LoadSequential, 1),
            ("store-sc", WarpManagedAtomicOpCode.StoreSequential, 2),
            ("compare-exchange", WarpManagedAtomicOpCode.CompareExchange, 3),
            ("exchange", WarpManagedAtomicOpCode.Exchange, 2),
            ("add", WarpManagedAtomicOpCode.Add, 2),
            ("load-acquire", WarpManagedAtomicOpCode.LoadAcquire, 1),
            ("store-release", WarpManagedAtomicOpCode.StoreRelease, 2),
            ("fence-sc", WarpManagedAtomicOpCode.Fence, 0),
        ];
        var result = new List<WarpLogicalMachineLayout>();
        foreach ((string name, WarpIrOpCode opCode, int arity) in operations)
        {
            var instructions = new List<WarpIrInstruction>();
            for (int argument = 0; argument < arity; argument++)
            {
                instructions.Add(new WarpIrInstruction(argument, WarpIrOpCode.LoadInput, immediate: (uint)argument));
            }

            instructions.Add(new WarpIrInstruction(arity, opCode, arity > 0 ? 0 : -1,
                arity > 1 ? 1 : -1, third: arity > 2 ? 2 : -1));
            result.Add(new WarpLogicalMachineLayout(new WarpControlFlowKernel(Semantics + "/" + name,
                Math.Max(1, arity), 0, [new WarpBasicBlock(0, [], instructions, new WarpReturnTerminator(arity))])));
        }

        return result;
    }
}
