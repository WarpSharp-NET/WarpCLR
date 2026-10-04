using System.Diagnostics.CodeAnalysis;
using WarpCLR.Backend.AMDGPU;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Backend.NVPTX;
using WarpCLR.Backend.SPIRV;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers and instantiates this fixture through reflection.")]
internal sealed class WarpWideResultTests
{
    [TestMethod]
    public void WideResultReturnsBothWordsInOneInvocationAndClearsThemOnReset()
    {
        var layout = new WarpLogicalMachineLayout(Create(new WarpWideReturnTerminator(0, 1)));
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(1, 3);
        compiled.ExecuteQuantum([[0x01234567], [0xFFFABCDE]], [], 0, state, 1, 3);
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(0x01234567u, state[WarpLogicalMachineLayout.ResultOffset]);
        Assert.AreEqual(0xFFFABCDEu, state[WarpLogicalMachineLayout.ResultHighOffset]);
        Assert.AreEqual(0u, state[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
        layout.ResetState(state, 2);
        compiled.ExecuteQuantum([[1], [2]], [], 0, state, 1, 3);
        Assert.AreEqual(WarpLogicalMachineLayout.Faulted, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(0u, state[WarpLogicalMachineLayout.ResultOffset]);
        Assert.AreEqual(0u, state[WarpLogicalMachineLayout.ResultHighOffset]);
    }

    [TestMethod]
    public void EveryResultWordAndItsWidthParticipatesInIdentity()
    {
        string wide = WarpIrHash.Compute(Create(new WarpWideReturnTerminator(0, 1)));
        string reverse = WarpIrHash.Compute(Create(new WarpWideReturnTerminator(1, 0)));
        string narrow = WarpIrHash.Compute(Create(new WarpReturnTerminator(0)));
        Assert.AreNotEqual(wide, reverse, StringComparer.Ordinal);
        Assert.AreNotEqual(wide, narrow, StringComparer.Ordinal);
    }

    [TestMethod]
    public void MismatchedWideHelperSignaturesAndMissingHighWordsAreRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Create(new WarpWideReturnTerminator(0, 2)));
        var function = new WarpControlFlowFunction(0, "wide.helper", 2,
            [new WarpBasicBlock(0, [],
                [new WarpIrInstruction(0, WarpIrOpCode.LoadArgument), new WarpIrInstruction(1, WarpIrOpCode.LoadArgument, immediate: 1)],
                new WarpWideReturnTerminator(0, 1))]);
        Assert.ThrowsExactly<ArgumentException>(() => new WarpControlFlowKernel("wide.caller", 2, 0,
            [new WarpBasicBlock(0, [],
                [new WarpIrInstruction(0, WarpIrOpCode.LoadInput), new WarpIrInstruction(1, WarpIrOpCode.LoadInput, immediate: 1),
                    new WarpIrInstruction(2, WarpIrOpCode.Call, callee: 0, arguments: [0, 1])],
                new WarpReturnTerminator(2))], functions: [function]));
    }

    [TestMethod]
    public void WideCallUsesOneFrameAndOneReturnToPreserveTheEntireTuple()
    {
        var function = new WarpControlFlowFunction(0, "wide.identity", 2,
            [new WarpBasicBlock(0, [],
                [new WarpIrInstruction(0, WarpIrOpCode.LoadArgument), new WarpIrInstruction(1, WarpIrOpCode.LoadArgument, immediate: 1)],
                new WarpWideReturnTerminator(0, 1))]);
        var kernel = new WarpControlFlowKernel("wide.call", 2, 0,
            [new WarpBasicBlock(0, [],
                [new WarpIrInstruction(0, WarpIrOpCode.LoadInput), new WarpIrInstruction(1, WarpIrOpCode.LoadInput, immediate: 1),
                    new WarpIrInstruction(2, 0, [0, 1], 2)], new WarpWideReturnTerminator(2, 3))], functions: [function]);
        var layout = new WarpLogicalMachineLayout(kernel);
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(2, 7);
        compiled.ExecuteQuantum([[0x01234567], [0xFFFABCDE]], [], 0, state, 2, 4);
        Assert.AreEqual(WarpLogicalMachineLayout.Runnable, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(2u, state[WarpLogicalMachineLayout.DepthOffset]);
        int callFrame = WarpLogicalMachineLayout.HeaderWords + layout.FrameWords;
        Assert.AreEqual(2u, state[callFrame + WarpLogicalMachineLayout.FrameReturnWordCountOffset]);
        Assert.AreEqual(0u, state[WarpLogicalMachineLayout.ResultOffset]);
        Assert.AreEqual(0u, state[WarpLogicalMachineLayout.ResultHighOffset]);
        uint[] corrupt = (uint[])state.Clone();
        corrupt[callFrame + WarpLogicalMachineLayout.FrameReturnValueOffset] = 3;
        Assert.ThrowsExactly<ArgumentException>(() => compiled.ExecuteQuantum([[1], [2]], [], 0, corrupt, 2, 4));
        compiled.ExecuteQuantum([[0x01234567], [0xFFFABCDE]], [], 0, state, 2, 4);
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(0x01234567u, state[WarpLogicalMachineLayout.ResultOffset]);
        Assert.AreEqual(0xFFFABCDEu, state[WarpLogicalMachineLayout.ResultHighOffset]);
        Assert.AreEqual(0u, state[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
    }

    [TestMethod]
    public void VoidCallsReturnWithoutInventingAValueOrOverwritingCallerState()
    {
        var function = new WarpControlFlowFunction(0, "void.helper", 0,
            [new WarpBasicBlock(0, [], [], new WarpTupleReturnTerminator([]))]);
        var kernel = new WarpControlFlowKernel("void.call", 1, 0,
            [new WarpBasicBlock(0, [],
                [new WarpIrInstruction(0, WarpIrOpCode.LoadInput), new WarpIrInstruction(-1, 0, [], 0)], new WarpReturnTerminator(0))], functions: [function]);
        var layout = new WarpLogicalMachineLayout(kernel);
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(2, 4);
        compiled.ExecuteQuantum([[0xA5A55A5A]], [], 0, state, 2, 4);
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(0xA5A55A5Au, state[WarpLogicalMachineLayout.ResultOffset]);
        Assert.AreEqual(1u, state[WarpLogicalMachineLayout.DepthOffset]);
    }

    [TestMethod]
    public void AggregateResultsHaveReservedStorageBeyondEveryAdmittedFrame()
    {
        WarpLogicalMachineLayout layout = WarpPortableResultKernels.CreateTupleCall();
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(3, 9);
        int third = layout.GetResultWordOffset(2, 3);
        Assert.AreEqual(state.Length - 1, third);
        Assert.HasCount(WarpLogicalMachineLayout.HeaderWords + layout.FrameWords * 3 + 1, state);
        compiled.ExecuteQuantum([[0x01234567], [0x89ABCDEF], [0xFEDCBA98]], [], 0, state, 3, 9);
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(0x01234567u, state[layout.GetResultWordOffset(0, 3)]);
        Assert.AreEqual(0x89ABCDEFu, state[layout.GetResultWordOffset(1, 3)]);
        Assert.AreEqual(0xFEDCBA98u, state[third]);
        layout.ResetState(state, 9);
        Assert.AreEqual(0u, state[third]);
    }

    [TestMethod]
    [DataRow(WarpBackendKind.NVPTX)]
    [DataRow(WarpBackendKind.AMDGPU)]
    [DataRow(WarpBackendKind.SPIRV)]
    public void PortableNativeMachineEmitsBothWordsBeforeCompletion(WarpBackendKind backend)
    {
        var layout = new WarpLogicalMachineLayout(Create(new WarpWideReturnTerminator(0, 1)));
        string source = WarpPortableMachineEmitter.Emit(layout, backend);
        StringAssert.Contains(source, WarpLogicalMachineLayout.Version, StringComparison.Ordinal);
        Assert.IsFalse(source.Contains("float", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("double", StringComparison.Ordinal));
        int low = source.LastIndexOf("%state, i64 7", StringComparison.Ordinal);
        int high = source.LastIndexOf("%state, i64 8", StringComparison.Ordinal);
        int complete = source.LastIndexOf("store i32 1,", StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, low);
        Assert.IsGreaterThan(low, high);
        Assert.IsGreaterThan(high, complete);
    }

    [TestMethod]
    public void LegacySingleWordArtifactsExplicitlyRejectWideResults()
    {
        WarpControlFlowKernel kernel = Create(new WarpWideReturnTerminator(0, 1));
        foreach (IWarpBackendCompiler backend in new IWarpBackendCompiler[]
            { new CoreCLRBackendCompiler(), new NVPTXBackendCompiler(), new AMDGPUBackendCompiler(), new SPIRVBackendCompiler() })
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => backend.Compile(kernel));
        }

        Assert.ThrowsExactly<InvalidOperationException>(() => CoreCLRJitKernel.Compile(kernel));
    }

    private static WarpControlFlowKernel Create(WarpBlockTerminator terminator) => new("wide.result", 2, 0,
        [new WarpBasicBlock(0, [],
            [new WarpIrInstruction(0, WarpIrOpCode.LoadInput), new WarpIrInstruction(1, WarpIrOpCode.LoadInput, immediate: 1)],
            terminator)]);
}
