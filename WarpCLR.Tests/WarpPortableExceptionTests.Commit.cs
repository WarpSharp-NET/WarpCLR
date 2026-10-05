using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableExceptionTests
{
    [TestMethod]
    public void AFailedTransferStatusRejectsBeforeAnyDescriptorOrSourceOwnerMemoryRead()
    {
        WarpStateDispatchTarget[] targets = [new(0, 0)];
        WarpControlFlowFunction transfer = WarpPortableExceptionTransferLowerer.Create(0, targets);
        var kernel = new WarpControlFlowKernel("EhRejectedStatus", 3, 0,
            [new WarpBasicBlock(0, [], [new(0, WarpIrOpCode.LoadInput, immediate: 0), new(1, WarpIrOpCode.LoadInput, immediate: 1),
                new(2, WarpIrOpCode.LoadInput, immediate: 2), new(3, 0, [0, 1, 2], 1)], new WarpReturnTerminator(3))], null, [transfer],
            new WarpLogicalExecutionMetadata([new(0, false, [1]), new(0, true, new int[transfer.Blocks.Count])], frameOwners: true,
                runtimeStateAccess: true, nonlocalStateDispatch: true));
        var layout = new WarpLogicalMachineLayout(kernel); CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        foreach (int quantum in new[] { 31, 4096 })
        {
            uint[] arena = [0, 0, 0]; uint[] state = layout.CreateInitialState(8, 100000);
            uint[][] inputs = [[uint.MaxValue], [WarpPortableExceptionLayout.InvalidContinuation], [0]];
            for (int count = 0; count < 1000 && state[0] == WarpLogicalMachineLayout.Runnable; count++)
            { compiled.ExecuteManagedQuantum(inputs, [], 0, state, 8, Math.Max(quantum, layout.MaximumBlockCost), arena, CancellationToken.None); }
            Assert.AreEqual(WarpLogicalMachineLayout.Faulted, state[WarpLogicalMachineLayout.StatusOffset]);
            Assert.AreEqual(3u, state[WarpLogicalMachineLayout.FaultKindOffset]);
            CollectionAssert.AreEqual(new uint[] { 0, 0, 0 }, arena);
        }
    }
}
