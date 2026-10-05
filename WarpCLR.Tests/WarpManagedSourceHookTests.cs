using System.Diagnostics.CodeAnalysis;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Runtime.Host.Native;

namespace WarpCLR.Tests;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this internal fixture through reflection.")]
internal sealed class WarpManagedSourceHookTests
{
    [TestMethod]
    public void SourceBoundaryStopsBeforeChargeAndExecutesOnlyAcknowledgedSourceInstruction()
    {
        WarpLogicalMachineLayout layout = WarpManagedSourceHookKernels.CreateBoundaries();
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(1, 3);
        layout.SetSourceBoundaryMode(state, enabled: true);
        int boundaries = 0;
        while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable)
        {
            uint before = state[WarpLogicalMachineLayout.RemainingStepsLowOffset];
            core.ExecuteQuantum([[41]], [], 0, state, 1, 65536);
            if (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Completed) { break; }
            Assert.AreEqual(WarpLogicalMachineLayout.BeforeSourceBoundary, state[WarpLogicalMachineLayout.SourceBoundaryStateOffset]);
            Assert.IsLessThanOrEqualTo(3, ++boundaries);
            uint[] parked = (uint[])state.Clone();
            core.ExecuteQuantum([[41]], [], 0, state, 1, 65536);
            CollectionAssert.AreEqual(parked, state);
            Assert.AreEqual(boundaries == 1 ? before : before - 1, state[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
            WarpNativeMachineLaunch.ValidateReturnedStates(layout, state, 1, 1);
            WarpLogicalMachineLayout.AcknowledgeSourceBoundary(state);
        }
        Assert.AreEqual(3, boundaries);
        Assert.AreEqual(42u, state[WarpLogicalMachineLayout.ResultOffset]);
        Assert.AreEqual(0u, state[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
    }

    [TestMethod]
    public void BoundaryAcknowledgementRequiresTheActualOriginalSourceContinuation()
    {
        WarpLogicalMachineLayout layout = WarpManagedSourceHookKernels.CreateBoundaries();
        uint[] state = layout.CreateInitialState(1, 3);
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpLogicalMachineLayout.AcknowledgeSourceBoundary(state));
        state[WarpLogicalMachineLayout.SourceBoundaryStateOffset] = 1;
        Assert.ThrowsExactly<ArgumentException>(() => CoreCLRResumableKernel.Compile(layout).ExecuteQuantum([[0]], [], 0, state, 1, 65536));
    }

    [TestMethod]
    public void CompiledFrameBorrowReadsAndWritesCallerBytesThroughHelpers()
    {
        foreach (bool write in new[] { false, true })
        {
            WarpLogicalMachineLayout layout = WarpManagedSourceHookKernels.CreateByteAccess(write);
            CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
            foreach (uint offset in new uint[] { 0, 1, 2, 3, 4, uint.MaxValue })
            {
                uint[] initial = layout.CreateInitialState(1, 1);
                uint[] small = Run(core, (uint[])initial.Clone(), [[offset], [0x1EF]], layout.MaximumBlockCost);
                uint[] large = Run(core, (uint[])initial.Clone(), [[offset], [0x1EF]], 65536);
                CollectionAssert.AreEqual(small, large);
                bool valid = offset < 4;
                Assert.AreEqual(valid ? 0u : WarpPortableFrameServices.InvalidSpan, small[WarpLogicalMachineLayout.ResultOffset]);
                uint expected = valid && write ? (0xA1B2C3D4u & ~(255u << (int)(offset * 8))) | (239u << (int)(offset * 8)) : 0xA1B2C3D4;
                Assert.AreEqual(expected, small[WarpLogicalMachineLayout.HeaderWords + layout.PrivateOffset]);
                uint result = valid ? write ? 239 : (0xA1B2C3D4u >> (int)(offset * 8)) & 255 : 0;
                Assert.AreEqual(result, small[WarpLogicalMachineLayout.ResultHighOffset]);
                WarpNativeMachineLaunch.ValidateReturnedStates(layout, small, 1, 1);
            }
        }
    }

    [TestMethod]
    public void FrameBorrowRejectsRetiredReusedForeignAndOutOfSpanOwners()
    {
        WarpLogicalMachineLayout layout = WarpManagedSourceHookKernels.CreateByteAccess(write: false);
        uint[] state = layout.CreateInitialState(1, 1);
        uint context = state[WarpLogicalMachineLayout.OwnerContextOffset];
        Assert.AreEqual(0u, WarpPortableFrameServices.ReadByte(state, context, 1, 1, 0, 4, 1, 0));
        Assert.AreEqual(WarpPortableFrameServices.InvalidOwner, WarpPortableFrameServices.ReadByte(state, context + 1, 1, 1, 0, 4, 1, 0));
        Assert.AreEqual(WarpPortableFrameServices.InvalidOwner, WarpPortableFrameServices.ReadByte(state, context, 2, 1, 0, 4, 1, 0));
        state[WarpLogicalMachineLayout.HeaderWords + WarpLogicalMachineLayout.FrameActivationOffset] = 2;
        Assert.AreEqual(WarpPortableFrameServices.InvalidOwner, WarpPortableFrameServices.ReadByte(state, context, 1, 1, 0, 4, 1, 0));
        layout.ResetState(state, 1);
        Assert.AreNotEqual(context, state[WarpLogicalMachineLayout.OwnerContextOffset]);
        Assert.AreEqual(WarpPortableFrameServices.InvalidOwner, WarpPortableFrameServices.ReadByte(state, context, 1, 1, 0, 4, 1, 0));
        context = state[WarpLogicalMachineLayout.OwnerContextOffset];
        Assert.AreEqual(WarpPortableFrameServices.InvalidSpan, WarpPortableFrameServices.WriteByte(state, context, 1, 1, uint.MaxValue, 4, 1, 0, 1));
        Assert.AreEqual(WarpPortableFrameServices.InvalidType, WarpPortableFrameServices.ReadByte(state, context, 1, 1, 0, 4, 0, 0));
    }

    [TestMethod]
    public void CompiledOwnerProjectionValidatesActivationWithoutChangingTheBorrowedFrame()
    {
        WarpLogicalMachineLayout borrowed = WarpManagedSourceHookKernels.CreateByteAccess(write: false);
        uint[] worker = borrowed.CreateInitialState(1, 1);
        worker[WarpLogicalMachineLayout.InteriorResultOffset] = 0xCAFEBABE;
        worker[WarpLogicalMachineLayout.InteriorFaultOffset] = 17;
        uint[] unchanged = (uint[])worker.Clone();
        WarpLogicalMachineLayout validation = WarpWordArenaServiceLowerer.Lower(typeof(WarpPortableFrameServices)
            .GetMethod(nameof(WarpPortableFrameServices.ValidateOwner))!);
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(validation);
        foreach (uint generation in new uint[] { 1, 2, uint.MaxValue })
        {
            uint[] control = validation.CreateInitialState(16, 65536);
            uint[][] inputs = [[worker[WarpLogicalMachineLayout.OwnerContextOffset]], [1], [generation], [0], [4], [1]];
            while (control[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable)
            {
                core.ExecuteManagedQuantum(inputs, [], 0, control, 16, validation.MaximumBlockCost, worker, CancellationToken.None);
            }
            Assert.AreEqual(WarpLogicalMachineLayout.Completed, control[WarpLogicalMachineLayout.StatusOffset]);
            Assert.AreEqual(generation == 1 ? 0u : WarpPortableFrameServices.InvalidOwner, control[WarpLogicalMachineLayout.ResultOffset]);
            CollectionAssert.AreEqual(unchanged, worker);
        }
    }

    [TestMethod]
    public void ActivationGenerationExhaustionCannotWrapOrEnterTheCallee()
    {
        WarpLogicalMachineLayout layout = WarpManagedSourceHookKernels.CreateByteAccess(write: false);
        uint[] state = layout.CreateInitialState(1, 1);
        state[WarpLogicalMachineLayout.NextActivationOffset] = uint.MaxValue;
        CoreCLRResumableKernel.Compile(layout).ExecuteQuantum([[0], [0]], [], 0, state, 1, 65536);
        Assert.AreEqual(WarpLogicalMachineLayout.Faulted, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(WarpLogicalMachineLayout.ActivationExhaustionFault, state[WarpLogicalMachineLayout.FaultKindOffset]);
        Assert.AreEqual(uint.MaxValue, state[WarpLogicalMachineLayout.NextActivationOffset]);
        Assert.AreEqual(1u, state[WarpLogicalMachineLayout.DepthOffset]);
        WarpNativeMachineLaunch.ValidateReturnedStates(layout, state, 1, 1);
    }

    [TestMethod]
    public void StateCapabilityChecksBoundsBeforeLoadingAnyWord()
    {
        WarpLogicalMachineLayout layout = WarpManagedSourceHookKernels.CreateStateBounds();
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        uint[] state = Run(core, layout.CreateInitialState(1, 1), [[uint.MaxValue]], 65536);
        Assert.AreEqual(WarpLogicalMachineLayout.ManagedMemoryBoundsFault, state[WarpLogicalMachineLayout.FaultKindOffset]);
        WarpNativeMachineLaunch.ValidateReturnedStates(layout, state, 1, 1);
    }

    [TestMethod]
    public void RuntimeWordCapabilitiesCannotBeInferredFromAnOrdinaryKernel()
    {
        WarpControlFlowKernel original = WarpManagedSourceHookKernels.CreateStateBounds().Kernel;
        Assert.ThrowsExactly<ArgumentException>(() => new WarpControlFlowKernel("forged", 1, 0, original.Blocks));
        Assert.ThrowsExactly<ArgumentException>(() => new WarpControlFlowKernel("forged", 1, 0, original.Blocks, null, null,
            new([new(0, false, [1])])));
    }

    private static uint[] Run(CoreCLRResumableKernel core, uint[] state, uint[][] inputs, int quantum)
    {
        int iterations = 0;
        while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable)
        {
            core.ExecuteQuantum(inputs, [], 0, state, 1, quantum);
            Assert.IsLessThan(10_000, ++iterations);
        }
        return state;
    }
}
