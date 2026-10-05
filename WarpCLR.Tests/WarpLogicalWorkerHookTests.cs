using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests;

[TestClass]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest creates this fixture through discovery.")]
internal sealed class WarpLogicalWorkerHookTests
{
    [TestMethod]
    public void ActualInvocationIndexIsDistinctFromFrameAndContextOwnership()
    {
        WarpLogicalMachineLayout direct = WarpLogicalWorkerHookKernels.CreateDirect();
        WarpLogicalMachineLayout nested = WarpLogicalWorkerHookKernels.CreateNested();
        foreach (int worker in new int[] { 0, 1, 63, 64, 255, 256, 65535, 65536, 0x40000000, int.MaxValue })
        {
            uint[] one = Run(direct, [], worker, 1, 65536);
            Assert.AreEqual((uint)worker, one[WarpLogicalMachineLayout.ResultOffset]);
            uint[] tiny = Run(nested, [], worker, 2, nested.MaximumBlockCost);
            uint[] large = Run(nested, [], worker, 2, 65536);
            CollectionAssert.AreEqual(tiny, large);
            Assert.AreEqual((uint)worker, tiny[WarpLogicalMachineLayout.ResultOffset]);
            Assert.AreEqual(0x80000000u, tiny[WarpLogicalMachineLayout.ResultOffset + 1]);
        }
    }

    [TestMethod]
    public void LogicalIndexUsesTheSameInputElementIncludingPartitionOffsets()
    {
        WarpLogicalMachineLayout layout = WarpLogicalWorkerHookKernels.CreateInputBinding();
        uint[] values = Enumerable.Range(0, 513).Select(index => (uint)index ^ 0xCAFEBABE).ToArray();
        foreach (int worker in new int[] { 0, 1, 63, 64, 255, 256, 511, 512 })
        {
            uint[] state = Run(layout, [values], worker, 1, 65536);
            Assert.AreEqual((uint)worker, state[WarpLogicalMachineLayout.ResultOffset]);
            Assert.AreEqual(values[worker], state[WarpLogicalMachineLayout.ResultOffset + 1]);
            Assert.AreEqual((uint)worker ^ values[worker], state[layout.GetResultWordOffset(2, 1)]);
        }
    }

    [TestMethod]
    public void MissingCapabilityIsRejectedInEntryAndEveryFunction()
    {
        WarpControlFlowKernel direct = WarpLogicalWorkerHookKernels.CreateDirect().Kernel;
        Assert.ThrowsExactly<ArgumentException>(() => new WarpControlFlowKernel(direct.Name, 1, 0, direct.Blocks));
        Assert.ThrowsExactly<ArgumentException>(() => Copy(direct, new WarpLogicalExecutionMetadata([new(0, false, [1])])));
        WarpControlFlowKernel nested = WarpLogicalWorkerHookKernels.CreateNested().Kernel;
        Assert.ThrowsExactly<ArgumentException>(() => Copy(nested, new WarpLogicalExecutionMetadata(nested.Execution!.Bodies, frameOwners: true)));
    }

    [TestMethod]
    public void InvocationQueryCannotCarryOperandsOffsetsOrTupleAndCallMetadata()
    {
        foreach (WarpIrInstruction instruction in new WarpIrInstruction[]
        {
            new(1, WarpManagedInvocationOpCode.LoadLogicalWorker, left: 0),
            new(1, WarpManagedInvocationOpCode.LoadLogicalWorker, immediate: 1),
            new(1, WarpManagedInvocationOpCode.LoadLogicalWorker, callee: 0),
            new(1, WarpManagedInvocationOpCode.LoadLogicalWorker, arguments: [0]),
        })
        {
            WarpBasicBlock[] blocks = [new(0, [], [new(0, WarpIrOpCode.Constant), instruction], new WarpReturnTerminator(1))];
            Assert.ThrowsExactly<ArgumentException>(() => new WarpControlFlowKernel("invalid-worker-query", 1, 0, blocks, null, [],
                new WarpLogicalExecutionMetadata([new(0, false, [1])], logicalWorkerAccess: true)));
        }
    }

    [TestMethod]
    public void CapabilityIsBoundToIrIdentityAndLegacyCodecCannotDiscardIt()
    {
        WarpBasicBlock[] blocks = [new(0, [], [new(0, WarpIrOpCode.Constant)], new WarpReturnTerminator(0))];
        var denied = new WarpControlFlowKernel("worker-identity-capability", 1, 0, blocks, null, [],
            new WarpLogicalExecutionMetadata([new(0, false, [1])]));
        WarpControlFlowKernel admitted = Copy(denied, new WarpLogicalExecutionMetadata([new(0, false, [1])], logicalWorkerAccess: true));
        Assert.AreNotEqual(WarpIrHash.Compute(denied), WarpIrHash.Compute(admitted), StringComparer.Ordinal);
        Assert.ThrowsExactly<NotSupportedException>(() => WarpCoreCLRPlanCodec.Serialize(admitted));
    }

    [TestMethod]
    public void NegativeInvocationIsRejectedBeforeAnyStateChange()
    {
        WarpLogicalMachineLayout layout = WarpLogicalWorkerHookKernels.CreateDirect();
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(1, 10);
        uint[] before = (uint[])state.Clone();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => core.ExecuteQuantum([], [], -1, state, 1, 65536));
        CollectionAssert.AreEqual(before, state);
    }

    private static WarpControlFlowKernel Copy(WarpControlFlowKernel kernel, WarpLogicalExecutionMetadata metadata) =>
        new(kernel.Name, Math.Max(1, kernel.InputBufferCount), kernel.ScalarArgumentCount, kernel.Blocks, kernel.Reduction, kernel.Functions, metadata);

    private static uint[] Run(WarpLogicalMachineLayout layout, uint[][] inputs, int worker, int depth, int quantum)
    {
        uint[] state = layout.CreateInitialState(depth, 10);
        if (layout.HasFrameOwners) { state[WarpLogicalMachineLayout.OwnerContextOffset] = 0x76543210; }
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        int calls = 0;
        while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable)
        {
            core.ExecuteQuantum(inputs, [], worker, state, depth, quantum);
            Assert.IsLessThan(100, ++calls);
        }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        return state;
    }
}
