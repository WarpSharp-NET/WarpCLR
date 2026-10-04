using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Runtime.Host.Native;

namespace WarpCLR.Tests;

[TestClass]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest creates this fixture through discovery.")]
internal sealed class WarpLogicalFrameTests
{
    [TestMethod]
    public void RecursiveSourceActivationsKeepDistinctPrivateValuesAndExactCharges()
    {
        WarpLogicalMachineLayout layout = WarpLogicalFrameKernels.CreateRecursiveSum();
        foreach (uint value in new uint[] { 0, 1, 2, 8, 16 })
        {
            int depth = checked((int)value + 1);
            uint[] small = Run(layout, [[value]], depth, 100, layout.MaximumBlockCost);
            uint[] large = Run(layout, [[value]], depth, 100, 65536);
            CollectionAssert.AreEqual(small, large);
            Assert.AreEqual(value * (value + 1) / 2, small[WarpLogicalMachineLayout.ResultOffset]);
            Assert.AreEqual(WarpLogicalMachineLayout.Completed, small[WarpLogicalMachineLayout.StatusOffset]);
            Assert.AreEqual(100UL - 2 * (value + 1), Remaining(small));
            Assert.AreEqual(0u, small[WarpLogicalMachineLayout.LogicalDepthOffset]);
        }
    }

    [TestMethod]
    public void SourceRecursionFaultsAtTheSameLogicalDepthAcrossQuanta()
    {
        WarpLogicalMachineLayout layout = WarpLogicalFrameKernels.CreateRecursiveSum();
        uint[] small = Run(layout, [[5]], 3, 100, layout.MaximumBlockCost);
        CollectionAssert.AreEqual(small, Run(layout, [[5]], 3, 100, 65536));
        Assert.AreEqual(WarpLogicalMachineLayout.Faulted, small[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(WarpLogicalMachineLayout.CallDepthFault, small[WarpLogicalMachineLayout.FaultKindOffset]);
        Assert.AreEqual(3u, small[WarpLogicalMachineLayout.LogicalDepthOffset]);
        Assert.AreEqual(4u, small[WarpLogicalMachineLayout.DepthOffset]);
        Assert.AreEqual(1u, small[WarpLogicalMachineLayout.FaultFunctionOffset]);
        Assert.AreEqual(0u, small[WarpLogicalMachineLayout.ResultOffset]);
    }

    [TestMethod]
    public void RuntimeHelpersYieldWithoutSpendingSourceStepsOrSourceFrames()
    {
        WarpLogicalMachineLayout layout = WarpLogicalFrameKernels.CreateHelperLoop();
        uint[] state = layout.CreateInitialState(1, 1);
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        int invocations = 0;
        while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable)
        {
            core.ExecuteQuantum([[257]], [], 0, state, 1, layout.MaximumBlockCost);
            Assert.IsLessThan(1_000, ++invocations);
            Assert.IsLessThanOrEqualTo(1u, state[WarpLogicalMachineLayout.LogicalDepthOffset]);
            WarpNativeMachineLaunch.ValidateReturnedStates(layout, state, 1, 1);
        }
        Assert.IsGreaterThan(100, invocations);
        Assert.AreEqual(257u, state[WarpLogicalMachineLayout.ResultOffset]);
        Assert.AreEqual(0UL, Remaining(state));
        CollectionAssert.AreEqual(state, Run(layout, [[257]], 1, 1, 65536));
    }

    [TestMethod]
    public void PrivateWordsAreZeroedForEveryActivationAndStateReset()
    {
        WarpLogicalMachineLayout layout = WarpLogicalFrameKernels.CreatePrivateReset();
        uint[] state = Run(layout, [[0]], 2, 3, layout.MaximumBlockCost);
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(0u, state[WarpLogicalMachineLayout.ResultOffset]);
        state[WarpLogicalMachineLayout.HeaderWords + layout.PrivateOffset] = uint.MaxValue;
        layout.ResetState(state, 3);
        Assert.AreEqual(0u, state[WarpLogicalMachineLayout.HeaderWords + layout.PrivateOffset]);
        Assert.AreEqual(0u, state[WarpLogicalMachineLayout.UsedOperationsLowOffset]);
        CoreCLRResumableKernel.Compile(layout).ExecuteQuantum([[0]], [], 0, state, 2, 65536);
        Assert.AreEqual(0u, state[WarpLogicalMachineLayout.ResultOffset]);
    }

    [TestMethod]
    public void TupleTailFollowsAllPhysicalHelperFrames()
    {
        WarpLogicalMachineLayout layout = WarpLogicalFrameKernels.CreateTuple();
        uint[] state = Run(layout, [[0xFFFFFFFF], [0x80000000], [0x7FA01234]], 1, 1, 65536);
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(0x7FA01234u, state[layout.GetResultWordOffset(2, 1)]);
        Assert.AreEqual(state.Length - 1, layout.GetResultWordOffset(2, 1));
        Assert.IsGreaterThan(WarpLogicalMachineLayout.HeaderWords + layout.FrameWords, layout.GetResultWordOffset(2, 1));
    }

    [TestMethod]
    public void OperationalCounterCarriesAndFaultsInsteadOfWrapping()
    {
        WarpLogicalMachineLayout layout = WarpLogicalFrameKernels.CreateHelperLoop();
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        uint[] carry = layout.CreateInitialState(1, 1);
        carry[WarpLogicalMachineLayout.UsedOperationsLowOffset] = uint.MaxValue - 1;
        core.ExecuteQuantum([[0]], [], 0, carry, 1, layout.MaximumBlockCost);
        Assert.AreEqual(1u, carry[WarpLogicalMachineLayout.UsedOperationsHighOffset]);
        uint[] overflow = layout.CreateInitialState(1, 1);
        overflow[WarpLogicalMachineLayout.UsedOperationsLowOffset] = uint.MaxValue;
        overflow[WarpLogicalMachineLayout.UsedOperationsHighOffset] = uint.MaxValue;
        core.ExecuteQuantum([[0]], [], 0, overflow, 1, layout.MaximumBlockCost);
        Assert.AreEqual(WarpLogicalMachineLayout.Faulted, overflow[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(WarpLogicalMachineLayout.OperationalOverflowFault, overflow[WarpLogicalMachineLayout.FaultKindOffset]);
        WarpNativeMachineLaunch.ValidateReturnedStates(layout, overflow, 1, 1);
    }

    [TestMethod]
    public void CorruptSourceDepthIsRejectedBeforeExecutingAnyOperation()
    {
        WarpLogicalMachineLayout layout = WarpLogicalFrameKernels.CreateHelperLoop();
        uint[] state = layout.CreateInitialState(1, 1);
        state[WarpLogicalMachineLayout.LogicalDepthOffset] = 1;
        uint[] before = (uint[])state.Clone();
        Assert.ThrowsExactly<ArgumentException>(() => CoreCLRResumableKernel.Compile(layout).ExecuteQuantum([[0]], [], 0, state, 1, 65536));
        CollectionAssert.AreEqual(before, state);
        Assert.ThrowsExactly<WarpHostException>(() => WarpNativeMachineLaunch.ValidateReturnedStates(layout, state, 1, 1));
    }

    [TestMethod]
    public void LegacySerializationCannotDiscardLogicalExecutionMetadata()
    {
        Assert.ThrowsExactly<NotSupportedException>(() => WarpCoreCLRPlanCodec.Serialize(WarpLogicalFrameKernels.CreateHelperLoop().Kernel));
    }

    [TestMethod]
    public void MetadataCannotAuthorizeAnotherBodyPrivateBankOrUnchargedHelperCycles()
    {
        WarpControlFlowKernel recursive = WarpLogicalFrameKernels.CreateRecursiveSum().Kernel;
        Assert.ThrowsExactly<ArgumentException>(() => Copy(recursive,
            [new(2, true, [0]), new(0, false, [1, 1, 1])]));
        Assert.ThrowsExactly<ArgumentException>(() => Copy(recursive,
            [new(0, true, [0]), new(1, true, [0, 0, 0])]));
        Assert.ThrowsExactly<ArgumentException>(() => new WarpLogicalBodyMetadata(0, true, [1]));
        Assert.ThrowsExactly<ArgumentException>(() => Copy(recursive,
            [new(0, true, [0]), new(1, false, [1, 1, 1])], recursiveCalls: false));
        Assert.ThrowsExactly<ArgumentException>(() => new WarpControlFlowKernel(recursive.Name, 1, 0,
            recursive.Blocks, functions: recursive.Functions));
    }

    [TestMethod]
    public void PrivateStorageAndOriginalSourceChargesArePartOfIrIdentity()
    {
        WarpControlFlowKernel recursive = WarpLogicalFrameKernels.CreateRecursiveSum().Kernel;
        string original = WarpIrHash.Compute(recursive);
        string largerPrivate = WarpIrHash.Compute(Copy(recursive, [new(0, true, [0]), new(2, false, [1, 1, 1])]));
        string differentCharge = WarpIrHash.Compute(Copy(recursive, [new(0, true, [0]), new(1, false, [2, 1, 1])]));
        Assert.AreNotEqual(original, largerPrivate, StringComparer.Ordinal);
        Assert.AreNotEqual(original, differentCharge, StringComparer.Ordinal);
    }

    private static WarpControlFlowKernel Copy(WarpControlFlowKernel kernel, WarpLogicalBodyMetadata[] bodies, bool recursiveCalls = true) =>
        new(kernel.Name, kernel.InputBufferCount, kernel.ScalarArgumentCount, kernel.Blocks, kernel.Reduction, kernel.Functions,
            new WarpLogicalExecutionMetadata(bodies, recursiveCalls));

    private static uint[] Run(WarpLogicalMachineLayout layout, uint[][] inputs, int depth, long steps, int quantum)
    {
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(depth, steps);
        int invocations = 0;
        do
        {
            core.ExecuteQuantum(inputs, [], 0, state, depth, quantum);
            Assert.IsLessThan(100_000, ++invocations);
        } while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable);
        return state;
    }

    private static ulong Remaining(uint[] state) => state[WarpLogicalMachineLayout.RemainingStepsLowOffset] |
        ((ulong)state[WarpLogicalMachineLayout.RemainingStepsHighOffset] << 32);
}
