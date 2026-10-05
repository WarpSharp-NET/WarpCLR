using System.Diagnostics.CodeAnalysis;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Runtime.Host.Native;

namespace WarpCLR.Tests;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this internal fixture through reflection.")]
internal sealed class WarpFilterAliasHookTests
{
    [TestMethod]
    public void FilterMutatesOriginalPrefixAndKeepsItsEvaluationWordsSeparate()
    {
        WarpLogicalMachineLayout layout = WarpFilterAliasHookKernels.Create();
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        foreach (uint value in new uint[] { 0, 1, 0x80000000, 0x7FA12345, uint.MaxValue })
        {
            uint[] initial = layout.CreateInitialState(1, 3);
            uint[] small = Run(core, (uint[])initial.Clone(), value, 1, layout.MaximumBlockCost);
            uint[] large = Run(core, (uint[])initial.Clone(), value, 1, 65536);
            CollectionAssert.AreEqual(small, large);
            Assert.AreEqual(WarpLogicalMachineLayout.Completed, small[WarpLogicalMachineLayout.StatusOffset]);
            Assert.AreEqual(unchecked(value + 1), small[WarpLogicalMachineLayout.ResultOffset]);
            Assert.AreEqual(WarpFilterAliasHookKernels.OriginalTail, small[WarpLogicalMachineLayout.ResultHighOffset]);
            Assert.AreEqual(WarpFilterAliasHookKernels.PrefixSentinel, small[WarpFilterAliasHookKernels.PrefixShadowResult]);
            Assert.AreEqual(WarpFilterAliasHookKernels.EvaluationSentinel, small[WarpFilterAliasHookKernels.EvaluationResult]);
            Assert.AreEqual(1u, small[WarpFilterAliasHookKernels.OwnerDepthResult]);
            Assert.AreEqual(1u, small[WarpFilterAliasHookKernels.OwnerActivationResult]);
            Assert.AreEqual(initial[WarpLogicalMachineLayout.OwnerContextOffset], small[WarpFilterAliasHookKernels.OwnerContextResult]);
            Assert.AreEqual(1u, small[WarpLogicalMachineLayout.LogicalDepthOffset]);
            Assert.AreEqual(0u, small[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
            WarpNativeMachineLaunch.ValidateReturnedStates(layout, small, 1, 1);
        }
    }

    [TestMethod]
    public void FilterHasOriginalSourceBoundariesWhileUsingNoAdditionalSourceCallDepth()
    {
        WarpLogicalMachineLayout layout = WarpFilterAliasHookKernels.Create();
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(1, 3);
        layout.SetSourceBoundaryMode(state, enabled: true);
        int boundaries = 0;
        while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable)
        {
            core.ExecuteQuantum([[41]], [], 0, state, 1, 65536);
            if (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Completed) { break; }
            WarpNativeMachineLaunch.ValidateReturnedStates(layout, state, 1, 1);
            Assert.AreEqual(1u, state[WarpLogicalMachineLayout.LogicalDepthOffset]);
            Assert.IsLessThanOrEqualTo(3, ++boundaries);
            if (boundaries == 2)
            {
                Assert.AreEqual(3u, state[WarpLogicalMachineLayout.DepthOffset]);
                Assert.AreEqual(41u, state[WarpLogicalMachineLayout.HeaderWords + layout.PrivateOffset]);
                Assert.AreEqual(2u, state[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
            }
            uint[] parked = (uint[])state.Clone();
            core.ExecuteQuantum([[41]], [], 0, state, 1, 65536);
            CollectionAssert.AreEqual(parked, state);
            WarpLogicalMachineLayout.AcknowledgeSourceBoundary(state);
        }
        Assert.AreEqual(3, boundaries);
        Assert.AreEqual(42u, state[WarpLogicalMachineLayout.ResultOffset]);
    }

    [TestMethod]
    public void SourceMethodCalledFromFilterConsumesTheRealSourceDepthLimit()
    {
        WarpLogicalMachineLayout layout = WarpFilterAliasHookKernels.Create(callSource: true);
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        uint[] denied = Run(core, layout.CreateInitialState(1, 4), 41, 1, 65536);
        Assert.AreEqual(WarpLogicalMachineLayout.CallDepthFault, denied[WarpLogicalMachineLayout.FaultKindOffset]);
        Assert.AreEqual(1u, denied[WarpLogicalMachineLayout.LogicalDepthOffset]);
        WarpNativeMachineLaunch.ValidateReturnedStates(layout, denied, 1, 1);
        uint[] admitted = Run(core, layout.CreateInitialState(2, 4), 41, 2, 65536);
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, admitted[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(42u, admitted[WarpLogicalMachineLayout.ResultOffset]);
        Assert.AreEqual(1u, admitted[WarpLogicalMachineLayout.LogicalDepthOffset]);
        Assert.AreEqual(0u, admitted[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
        WarpNativeMachineLaunch.ValidateReturnedStates(layout, admitted, 1, 2);
    }

    [TestMethod]
    public void FilterStillConsumesTheSourceBudgetBeforeItsFirstPrivateWrite()
    {
        WarpLogicalMachineLayout layout = WarpFilterAliasHookKernels.Create();
        uint[] state = Run(CoreCLRResumableKernel.Compile(layout), layout.CreateInitialState(1, 1), 41, 1, 65536);
        Assert.AreEqual(WarpLogicalMachineLayout.StepLimitFault, state[WarpLogicalMachineLayout.FaultKindOffset]);
        Assert.AreEqual(41u, state[WarpLogicalMachineLayout.HeaderWords + layout.PrivateOffset]);
        Assert.AreEqual(0u, state[WarpFilterAliasHookKernels.OwnerDepthResult]);
        Assert.AreEqual(1u, state[WarpLogicalMachineLayout.LogicalDepthOffset]);
        WarpNativeMachineLaunch.ValidateReturnedStates(layout, state, 1, 1);
    }

    [TestMethod]
    public void GeneratedAliasGuardRejectsForeignRetiredAndSelfOwnersBeforeAnyFilterWrite()
    {
        foreach ((uint depth, uint activation) in new (uint, uint)[] { (0, 1), (3, 1), (uint.MaxValue, 1), (1, 0), (1, 2), (1, uint.MaxValue) })
        {
            WarpLogicalMachineLayout layout = WarpFilterAliasHookKernels.Create(ownerDepth: depth, ownerActivation: activation);
            uint[] state = Run(CoreCLRResumableKernel.Compile(layout), layout.CreateInitialState(1, 3), 41, 1, 65536);
            Assert.AreEqual(WarpLogicalMachineLayout.Faulted, state[WarpLogicalMachineLayout.StatusOffset]);
            Assert.AreEqual(3u, state[WarpLogicalMachineLayout.FaultKindOffset]);
            Assert.AreEqual(41u, state[WarpLogicalMachineLayout.HeaderWords + layout.PrivateOffset]);
            Assert.AreEqual(0u, state[WarpFilterAliasHookKernels.OwnerDepthResult]);
            Assert.ThrowsExactly<WarpHostException>(() => WarpNativeMachineLaunch.ValidateReturnedStates(layout, state, 1, 1));
        }
    }

    [TestMethod]
    public void FilterAliasMetadataRequiresAnExactLiveSourcePrefixAndNonlocalEntry()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new WarpLogicalBodyMetadata(2, true, [0], aliasOwnerFunction: 0, aliasPrefixWords: 1));
        Assert.ThrowsExactly<ArgumentException>(() => new WarpLogicalBodyMetadata(2, false, [1], countsSourceDepth: false));
        WarpControlFlowKernel original = WarpFilterAliasHookKernels.Create().Kernel;
        Assert.ThrowsExactly<ArgumentException>(() => new WarpControlFlowKernel("unadmitted-alias", 1, 0,
            original.Blocks, reduction: null, original.Functions, new WarpLogicalExecutionMetadata(original.Execution!.Bodies,
                frameOwners: true, runtimeStateAccess: true)));
        Assert.IsFalse(original.Execution!.Bodies[2].RuntimeHelper);
        Assert.IsFalse(original.Execution.Bodies[2].CountsSourceDepth);
        Assert.AreEqual(1, original.Execution.Bodies[2].SourceBlockCosts[0]);
    }

    [TestMethod]
    public void AlteredOwnerOrReturnStorageCannotWriteThroughAParkedFilter()
    {
        WarpLogicalMachineLayout layout = WarpFilterAliasHookKernels.Create();
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        int owner = WarpLogicalMachineLayout.HeaderWords;
        int alias = owner + 2 * layout.FrameWords;
        foreach ((int offset, uint value) in new (int, uint)[]
        {
            (owner + WarpLogicalMachineLayout.FrameFunctionOffset, 1),
            (owner + WarpLogicalMachineLayout.FrameActivationOffset, 2),
            (owner + WarpLogicalMachineLayout.FramePrivateWordsOffset, 1),
            (alias + WarpLogicalMachineLayout.FrameReturnValueOffset, 1),
            (alias + WarpLogicalMachineLayout.FrameReturnWordCountOffset, 1),
        })
        {
            uint[] state = ParkFilter(core);
            state[offset] = value;
            WarpLogicalMachineLayout.AcknowledgeSourceBoundary(state);
            RejectPublicInputAndExecuteGeneratedGuard(core, state);
            Assert.AreEqual(WarpLogicalMachineLayout.Faulted, state[WarpLogicalMachineLayout.StatusOffset]);
            Assert.AreEqual(3u, state[WarpLogicalMachineLayout.FaultKindOffset]);
            Assert.AreEqual(41u, state[owner + layout.PrivateOffset]);
            Assert.AreEqual(2u, state[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
            Assert.AreEqual(0u, state[WarpFilterAliasHookKernels.OwnerDepthResult]);
            Assert.ThrowsExactly<WarpHostException>(() => WarpNativeMachineLaunch.ValidateReturnedStates(layout, state, 1, 1));
        }
    }

    [TestMethod]
    public void ASecondLiveAliasForTheSameOwnerIsRejectedBeforeTheSourceCharge()
    {
        WarpLogicalMachineLayout layout = WarpFilterAliasHookKernels.Create();
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        uint[] state = ParkFilter(core);
        int previous = WarpLogicalMachineLayout.HeaderWords + layout.FrameWords;
        state[previous + WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset] = 1;
        state[previous + WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset] = 1;
        WarpLogicalMachineLayout.AcknowledgeSourceBoundary(state);
        RejectPublicInputAndExecuteGeneratedGuard(core, state);
        Assert.AreEqual(3u, state[WarpLogicalMachineLayout.FaultKindOffset]);
        Assert.AreEqual(41u, state[WarpLogicalMachineLayout.HeaderWords + layout.PrivateOffset]);
        Assert.AreEqual(2u, state[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
        Assert.AreEqual(0u, state[WarpFilterAliasHookKernels.OwnerDepthResult]);
        Assert.ThrowsExactly<WarpHostException>(() => WarpNativeMachineLaunch.ValidateReturnedStates(layout, state, 1, 1));
    }

    [TestMethod]
    public void AnAliasCannotBeEnteredOrLeftAsAnOrdinaryMethodCall()
    {
        WarpControlFlowKernel kernel = WarpFilterAliasHookKernels.Create().Kernel;
        WarpBasicBlock root = kernel.Blocks[0];
        WarpBasicBlock[] directCall = kernel.Blocks.ToArray();
        directCall[0] = new(root.Id, root.Parameters,
            [new(0, WarpIrOpCode.Call, callee: 1)], root.Terminator);
        Assert.ThrowsExactly<ArgumentException>(() => kernel.Execution!.Validate(directCall, kernel.Functions));
        WarpControlFlowFunction filter = kernel.Functions[1];
        foreach (WarpBlockTerminator ordinary in new WarpBlockTerminator[] { new WarpReturnTerminator(0), new WarpTupleReturnTerminator([0]) })
        {
            WarpControlFlowFunction[] functions = kernel.Functions.ToArray();
            WarpBasicBlock block = filter.Blocks[0];
            functions[1] = new(filter.Id, filter.Name, filter.ParameterCount,
                [new(block.Id, block.Parameters, block.Instructions, ordinary)]);
            Assert.ThrowsExactly<ArgumentException>(() => kernel.Execution!.Validate(kernel.Blocks, functions));
        }
    }

    private static uint[] ParkFilter(CoreCLRResumableKernel core)
    {
        uint[] state = core.Layout.CreateInitialState(1, 3);
        core.Layout.SetSourceBoundaryMode(state, enabled: true);
        core.ExecuteQuantum([[41]], [], 0, state, 1, 65536);
        WarpLogicalMachineLayout.AcknowledgeSourceBoundary(state);
        core.ExecuteQuantum([[41]], [], 0, state, 1, 65536);
        Assert.AreEqual(WarpLogicalMachineLayout.BeforeSourceBoundary, state[WarpLogicalMachineLayout.SourceBoundaryStateOffset]);
        Assert.AreEqual(3u, state[WarpLogicalMachineLayout.DepthOffset]);
        Assert.AreEqual(41u, state[WarpLogicalMachineLayout.HeaderWords + core.Layout.PrivateOffset]);
        WarpNativeMachineLaunch.ValidateReturnedStates(core.Layout, state, 1, 1);
        return state;
    }

    private static void RejectPublicInputAndExecuteGeneratedGuard(CoreCLRResumableKernel core, uint[] state)
    {
        uint[] before = (uint[])state.Clone();
        Assert.ThrowsExactly<ArgumentException>(() => core.ExecuteQuantum([[41]], [], 0, state, 1, 65536));
        CollectionAssert.AreEqual(before, state);
        var generated = core.CompiledEntryPoint.CreateDelegate<Action<uint[][], uint[], int, uint[], int, int, CancellationToken, uint[]>>();
        generated([[41]], [], 0, state, 1, 65536, CancellationToken.None, []);
    }

    private static uint[] Run(CoreCLRResumableKernel core, uint[] state, uint value, int depth, int quantum)
    {
        int quanta = 0;
        do
        {
            core.ExecuteQuantum([[value]], [], 0, state, depth, quantum);
            Assert.IsLessThan(100, ++quanta);
        } while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable);
        return state;
    }
}
