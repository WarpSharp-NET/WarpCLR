using System.Diagnostics.CodeAnalysis;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host.Native;

namespace WarpCLR.Tests;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this internal fixture through reflection.")]
internal sealed class WarpExceptionMachineHookTests
{
    [TestMethod]
    public void NonlocalTransferRejectsInvalidDepthAndUndeclaredDestinationsBeforeDispatch()
    {
        foreach (WarpLogicalMachineLayout layout in WarpExceptionMachineHookKernels.Create().Skip(2))
        {
            uint[] state = layout.CreateInitialState(1, 1);
            CoreCLRResumableKernel.Compile(layout).ExecuteQuantum([[0]], [], 0, state, 1, 65536);
            Assert.AreEqual(WarpLogicalMachineLayout.Faulted, state[WarpLogicalMachineLayout.StatusOffset]);
            Assert.AreEqual(3u, state[WarpLogicalMachineLayout.FaultKindOffset]);
            Assert.AreEqual(0u, state[WarpLogicalMachineLayout.ResultOffset]);
        }
    }

    [TestMethod]
    public void NonlocalTransferRefreshesDepthAndPcWithoutCopyingTheAbandonedCallResult()
    {
        WarpLogicalMachineLayout layout = WarpExceptionMachineHookKernels.CreateUnwindTransfer();
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        foreach (int quantum in new[] { layout.MaximumBlockCost, 65536 })
        {
            uint[] state = layout.CreateInitialState(1, 2);
            int abandonedResult = WarpLogicalMachineLayout.HeaderWords + WarpLogicalMachineLayout.FrameHeaderWords + 1;
            state[abandonedResult] = 0x80000001;
            int quanta = 0;
            do
            {
                core.ExecuteQuantum([[0x7FA12345]], [], 0, state, 1, quantum);
                WarpNativeMachineLaunch.ValidateReturnedStates(layout, state, 1, 1);
                Assert.IsLessThan(100, ++quanta);
            } while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable);
            Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
            Assert.AreEqual(99u, state[WarpLogicalMachineLayout.ResultOffset]);
            Assert.AreEqual(0x80000001u, state[abandonedResult]);
            Assert.AreEqual(1u, state[WarpLogicalMachineLayout.DepthOffset]);
            Assert.AreEqual(0u, state[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
        }
    }

    [TestMethod]
    public void TerminalTransferKeepsTheRuntimeWrittenResultWithoutAnOrdinaryReturn()
    {
        WarpLogicalMachineLayout layout = WarpExceptionMachineHookKernels.CreateTerminalTransfer();
        uint[] state = layout.CreateInitialState(1, 1);
        CoreCLRResumableKernel.Compile(layout).ExecuteQuantum([[0]], [], 0, state, 1, 65536);
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(0x7FA12345u, state[WarpLogicalMachineLayout.ResultOffset]);
        Assert.AreEqual(1u, state[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
        WarpNativeMachineLaunch.ValidateReturnedStates(layout, state, 1, 1);
    }

    [TestMethod]
    public void NonlocalDispatchCannotBeAdmittedWithoutEveryExplicitCapability()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new WarpLogicalExecutionMetadata(
            [new WarpLogicalBodyMetadata(0, true, [0])], frameOwners: true, nonlocalStateDispatch: true));
        Assert.ThrowsExactly<ArgumentException>(() => new WarpLogicalExecutionMetadata(
            [new WarpLogicalBodyMetadata(0, true, [0])], runtimeStateAccess: true, nonlocalStateDispatch: true));
        Assert.ThrowsExactly<ArgumentException>(() => new WarpControlFlowKernel("unadmitted", 1, 0,
            [new WarpBasicBlock(0, [], [], new WarpStateDispatchTerminator([new(0, 0)]))]));
        Assert.ThrowsExactly<ArgumentException>(() => new WarpControlFlowKernel("unregistered-destination", 1, 0,
            [new WarpBasicBlock(0, [], [], new WarpStateDispatchTerminator([new(9, 0)]))], reduction: null, functions: null,
            new WarpLogicalExecutionMetadata([new WarpLogicalBodyMetadata(0, true, [0])],
                frameOwners: true, runtimeStateAccess: true, nonlocalStateDispatch: true)));
    }

    [TestMethod]
    public void NonlocalDestinationsAreOwnedAndBoundToTheCompiledIrIdentity()
    {
        WarpStateDispatchTarget[] targets = [new(0, 0)];
        var dispatch = new WarpStateDispatchTerminator(targets);
        targets[0] = new(7, 7);
        Assert.AreEqual(new WarpStateDispatchTarget(0, 0), dispatch.Destinations[0]);
        Assert.ThrowsExactly<ArgumentException>(() => new WarpStateDispatchTerminator([new(0, 0), new(0, 0)]));
        WarpControlFlowKernel kernel = WarpExceptionMachineHookKernels.CreateUnwindTransfer().Kernel;
        Assert.AreEqual(WarpIrHash.Compute(kernel), WarpIrHash.Compute(WarpExceptionMachineHookKernels.CreateUnwindTransfer().Kernel), StringComparer.Ordinal);
        Assert.IsTrue(kernel.Execution!.NonlocalStateDispatch);
    }
}
