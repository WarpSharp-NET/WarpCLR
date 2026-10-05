using System.Diagnostics.CodeAnalysis;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Runtime.Host.Native;

namespace WarpCLR.Tests;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this internal fixture through reflection.")]
internal sealed class WarpManagedExceptionHookTests
{
    [TestMethod]
    public void EscapedObjectIsTerminalWithoutPublishingAnyNormalResult()
    {
        foreach (WarpLogicalMachineLayout layout in new[] { WarpManagedExceptionHookKernels.Create(0), WarpManagedExceptionHookKernels.Create(1),
            WarpManagedExceptionHookKernels.Create(3), WarpManagedExceptionHookKernels.CreateHelper() })
        {
            CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
            uint[] initial = Initial(layout);
            uint[] small = Run(core, (uint[])initial.Clone(), [0x1234, 42, uint.MaxValue], layout.MaximumBlockCost);
            uint[] large = Run(core, (uint[])initial.Clone(), [0x1234, 42, uint.MaxValue], 65536);
            CollectionAssert.AreEqual(small, large);
            Assert.AreEqual(WarpLogicalMachineLayout.Faulted, small[WarpLogicalMachineLayout.StatusOffset]);
            Assert.AreEqual(WarpLogicalMachineLayout.ManagedExceptionFault, small[WarpLogicalMachineLayout.FaultKindOffset]);
            Assert.AreEqual(0x1234u, small[WarpLogicalMachineLayout.EscapedExceptionContextOffset]);
            Assert.AreEqual(42u, small[WarpLogicalMachineLayout.EscapedExceptionObjectOffset]);
            Assert.AreEqual(uint.MaxValue, small[WarpLogicalMachineLayout.EscapedExceptionGenerationOffset]);
            AssertResultCanaries(layout, small);
            WarpNativeMachineLaunch.ValidateReturnedStates(layout, small, 1, 2);
            uint[] ended = (uint[])small.Clone();
            core.ExecuteManagedQuantum([[99], [99], [99]], [], 0, small, 2, 65536, [0xCAFE]);
            CollectionAssert.AreEqual(ended, small);
        }
    }

    [TestMethod]
    public void NullOrIncompleteEscapedReferencesFailBeforePayloadOrResultPublication()
    {
        WarpLogicalMachineLayout layout = WarpManagedExceptionHookKernels.Create();
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        foreach (uint[] reference in new uint[][] { [0, 1, 1], [1, 0, 1], [1, 1, 0], [0, 0, 0] })
        foreach (int quantum in new[] { layout.MaximumBlockCost, 65536 })
        {
            uint[] state = Run(core, Initial(layout), reference, quantum);
            Assert.AreEqual(3u, state[WarpLogicalMachineLayout.FaultKindOffset]);
            Assert.AreEqual(0u, state[WarpLogicalMachineLayout.EscapedExceptionContextOffset]);
            Assert.AreEqual(0u, state[WarpLogicalMachineLayout.EscapedExceptionObjectOffset]);
            Assert.AreEqual(0u, state[WarpLogicalMachineLayout.EscapedExceptionGenerationOffset]);
            AssertResultCanaries(layout, state);
            Assert.ThrowsExactly<WarpHostException>(() => WarpNativeMachineLaunch.ValidateReturnedStates(layout, state, 1, 2));
        }
    }

    [TestMethod]
    public void SourceBoundaryAndBudgetCannotExposeAPartialExceptionHeader()
    {
        WarpLogicalMachineLayout layout = WarpManagedExceptionHookKernels.Create(sourceCost: 2);
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(2, 1);
        layout.SetSourceBoundaryMode(state, enabled: true);
        core.ExecuteManagedQuantum([[1], [2], [3]], [], 0, state, 2, 65536, [0xCAFE]);
        Assert.AreEqual(WarpLogicalMachineLayout.BeforeSourceBoundary, state[WarpLogicalMachineLayout.SourceBoundaryStateOffset]);
        WarpNativeMachineLaunch.ValidateReturnedStates(layout, state, 1, 2);
        uint[] parked = (uint[])state.Clone();
        core.ExecuteManagedQuantum([[1], [2], [3]], [], 0, state, 2, 65536, [0xCAFE]);
        CollectionAssert.AreEqual(parked, state);
        WarpLogicalMachineLayout.AcknowledgeSourceBoundary(state);
        core.ExecuteManagedQuantum([[1], [2], [3]], [], 0, state, 2, 65536, [0xCAFE]);
        Assert.AreEqual(WarpLogicalMachineLayout.StepLimitFault, state[WarpLogicalMachineLayout.FaultKindOffset]);
        Assert.AreEqual(0u, state[WarpLogicalMachineLayout.EscapedExceptionContextOffset]);
        Assert.AreEqual(0u, state[WarpLogicalMachineLayout.EscapedExceptionObjectOffset]);
        Assert.AreEqual(0u, state[WarpLogicalMachineLayout.EscapedExceptionGenerationOffset]);
        WarpNativeMachineLaunch.ValidateReturnedStates(layout, state, 1, 2);
    }

    [TestMethod]
    public void TerminationRequiresExactAdmissionAndAvailableSsaOperands()
    {
        WarpControlFlowKernel original = WarpManagedExceptionHookKernels.Create().Kernel;
        Assert.ThrowsExactly<ArgumentException>(() => new WarpControlFlowKernel("missing-exception-capability", 3, 0,
            original.Blocks, reduction: null, functions: null, new WarpLogicalExecutionMetadata(original.Execution!.Bodies,
                frameOwners: true, runtimeStateAccess: true, nonlocalStateDispatch: true)));
        Assert.ThrowsExactly<ArgumentException>(() => new WarpControlFlowKernel("missing-frame-capabilities", 3, 0,
            original.Blocks, reduction: null, functions: null));
        Assert.ThrowsExactly<ArgumentException>(() => new WarpLogicalExecutionMetadata(original.Execution!.Bodies, managedExceptionTermination: true));
        Assert.ThrowsExactly<ArgumentException>(() => new WarpControlFlowKernel("unbound-exception-operand", 3, 0,
            [new(0, [], original.Blocks[0].Instructions, new WarpManagedExceptionTerminator(0, 1, 3))], reduction: null,
            functions: null, original.Execution));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new WarpManagedExceptionTerminator(-1, 1, 2));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new WarpManagedExceptionTerminator(0, -1, 2));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new WarpManagedExceptionTerminator(0, 1, -1));
        Assert.AreEqual(0, WarpManagedExceptionHookKernels.Create(0).ResultWordCount);
        Assert.AreEqual(3, WarpManagedExceptionHookKernels.Create(3).ResultWordCount);
    }

    [TestMethod]
    public void ForgedTerminalHeadersRemainRejectedForOrdinaryOrIncompleteStates()
    {
        WarpLogicalMachineLayout admitted = WarpManagedExceptionHookKernels.Create();
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(admitted);
        uint[] terminal = Run(core, Initial(admitted), [1, 2, 3], 65536);
        foreach (int offset in new[] { WarpLogicalMachineLayout.EscapedExceptionContextOffset, WarpLogicalMachineLayout.EscapedExceptionObjectOffset,
            WarpLogicalMachineLayout.EscapedExceptionGenerationOffset })
        {
            uint[] missing = (uint[])terminal.Clone();
            missing[offset] = 0;
            Assert.ThrowsExactly<WarpHostException>(() => WarpNativeMachineLaunch.ValidateReturnedStates(admitted, missing, 1, 2));
            Assert.ThrowsExactly<ArgumentException>(() => core.ExecuteManagedQuantum([[1], [2], [3]], [], 0, missing, 2, 65536, [0xCAFE]));
        }
        foreach (uint status in new[] { WarpLogicalMachineLayout.Runnable, WarpLogicalMachineLayout.Completed })
        {
            uint[] malformed = (uint[])terminal.Clone();
            malformed[WarpLogicalMachineLayout.StatusOffset] = status;
            Assert.ThrowsExactly<WarpHostException>(() => WarpNativeMachineLaunch.ValidateReturnedStates(admitted, malformed, 1, 2));
        }
        WarpControlFlowKernel ordinaryKernel = new("unadmitted-exception-header", 3, 0,
            [new(0, [], [new(0, WarpIrOpCode.LoadInput)], new WarpReturnTerminator(0))], reduction: null, functions: null,
            new WarpLogicalExecutionMetadata([new(0, false, [1])], frameOwners: true, runtimeStateAccess: true, nonlocalStateDispatch: true));
        var ordinary = new WarpLogicalMachineLayout(ordinaryKernel);
        uint[] forged = ordinary.CreateInitialState(2, 10);
        forged[WarpLogicalMachineLayout.StatusOffset] = WarpLogicalMachineLayout.Faulted;
        forged[WarpLogicalMachineLayout.FaultKindOffset] = WarpLogicalMachineLayout.ManagedExceptionFault;
        forged[WarpLogicalMachineLayout.EscapedExceptionContextOffset] = 1;
        forged[WarpLogicalMachineLayout.EscapedExceptionObjectOffset] = 2;
        forged[WarpLogicalMachineLayout.EscapedExceptionGenerationOffset] = 3;
        Assert.ThrowsExactly<WarpHostException>(() => WarpNativeMachineLaunch.ValidateReturnedStates(ordinary, forged, 1, 2));
    }

    [TestMethod]
    public void ExceptionCapabilityParticipatesInIrIdentityAndRequiresAnArena()
    {
        WarpBasicBlock[] blocks = [new(0, [], [new(0, WarpIrOpCode.LoadInput)], new WarpReturnTerminator(0))];
        WarpLogicalBodyMetadata[] bodies = [new(0, false, [1])];
        var ordinary = new WarpControlFlowKernel("exception-capability-identity", 1, 0, blocks, reduction: null, functions: null,
            new WarpLogicalExecutionMetadata(bodies, frameOwners: true, runtimeStateAccess: true, nonlocalStateDispatch: true));
        var admitted = new WarpControlFlowKernel(ordinary.Name, 1, 0, blocks, reduction: null, functions: null,
            new WarpLogicalExecutionMetadata(bodies, frameOwners: true, runtimeStateAccess: true, nonlocalStateDispatch: true, managedExceptionTermination: true));
        Assert.IsFalse(string.Equals(WarpIrHash.Compute(ordinary), WarpIrHash.Compute(admitted), StringComparison.Ordinal));
        var layout = new WarpLogicalMachineLayout(admitted);
        Assert.IsTrue(layout.RequiresManagedMemory);
        Assert.ThrowsExactly<ArgumentException>(() => CoreCLRResumableKernel.Compile(layout).ExecuteQuantum([[1]], [], 0, layout.CreateInitialState(2, 10), 2, 65536));
        Assert.ThrowsExactly<NotSupportedException>(() => WarpCoreCLRPlanCodec.Serialize(admitted));
    }

    private static uint[] Initial(WarpLogicalMachineLayout layout)
    {
        uint[] state = layout.CreateInitialState(2, 10);
        state[WarpLogicalMachineLayout.ResultOffset] = 0xBAD00BAD;
        state[WarpLogicalMachineLayout.ResultHighOffset] = 0x13579BDF;
        for (int word = 2; word < layout.ResultWordCount; word++) { state[layout.GetResultWordOffset(word, 2)] = 0xCAFEBABE; }
        return state;
    }

    private static void AssertResultCanaries(WarpLogicalMachineLayout layout, uint[] state)
    {
        Assert.AreEqual(0xBAD00BADu, state[WarpLogicalMachineLayout.ResultOffset]);
        Assert.AreEqual(0x13579BDFu, state[WarpLogicalMachineLayout.ResultHighOffset]);
        for (int word = 2; word < layout.ResultWordCount; word++) { Assert.AreEqual(0xCAFEBABEu, state[layout.GetResultWordOffset(word, 2)]); }
    }

    private static uint[] Run(CoreCLRResumableKernel core, uint[] state, uint[] reference, int quantum)
    {
        int quanta = 0;
        do
        {
            core.ExecuteManagedQuantum([[reference[0]], [reference[1]], [reference[2]]], [], 0, state, 2, quantum, [0xCAFE]);
            Assert.IsLessThan(100, ++quanta);
        } while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable);
        return state;
    }
}
