using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpCoreCLROrdinaryArrayEntryTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void PublicQuantumForwardingDeniesProtectedBanksBeforeTerminalHeaderReads(int role)
    {
        var layout = new WarpLogicalMachineLayout(NarrowKernel());
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        foreach (uint status in new[] { WarpLogicalMachineLayout.Runnable, WarpLogicalMachineLayout.Completed, WarpLogicalMachineLayout.Faulted })
        {
            uint[][] inputs = [[0xFFFFFFFF]];
            uint[] scalars = [0x7FC00001], state = layout.CreateInitialState(4, 100);
            state[WarpLogicalMachineLayout.StatusOffset] = status;
            uint[] before = (uint[])state.Clone();
            object owner = new(), authority = new();
            WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority,
                [SelectBank(role, inputs, scalars, state, [])]);
            WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
            Assert.ThrowsExactly<InvalidOperationException>(() => core.ExecuteQuantum(inputs, scalars, 0, state, 4, 100));
            CollectionAssert.AreEqual(before, state);
        }
    }

    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(0, 1)]
    [DataRow(0, 2)]
    [DataRow(0, 3)]
    [DataRow(1, 0)]
    [DataRow(1, 1)]
    [DataRow(1, 2)]
    [DataRow(1, 3)]
    [DataRow(2, 0)]
    [DataRow(2, 1)]
    [DataRow(2, 2)]
    [DataRow(2, 3)]
    public void ResumableEntriesRejectEveryOriginalArrayRoleBeforeMachineEffects(int route, int role)
    {
        var layout = new WarpLogicalMachineLayout(NarrowKernel());
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        foreach (bool quarantine in new[] { false, true })
        {
            uint[][] inputs = [[0x80000000]];
            uint[] scalars = [0x7FC00001], state = layout.CreateInitialState(4, 100), arena = [0xFEDCBA98];
            uint[] bank = SelectBank(role, inputs, scalars, state, arena);
            uint[] beforeState = (uint[])state.Clone(), beforeArena = (uint[])arena.Clone(), beforeBank = (uint[])bank.Clone();
            object owner = new(), authority = new();
            WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [bank]);
            WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
            if (quarantine) { WarpOrdinaryArrayRegistry.Quarantine(handle, owner, authority); }
            AssertDenied(() => InvokeQuantum(core, route, inputs, scalars, state, arena, 100), route == 2);
            CollectionAssert.AreEqual(beforeState, state);
            CollectionAssert.AreEqual(beforeArena, arena);
            CollectionAssert.AreEqual(beforeBank, bank);
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void TerminalFastReturnsRetainCrossRoleDenialAndPublishNoFurtherWords(int route)
    {
        var layout = new WarpLogicalMachineLayout(NarrowKernel());
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        foreach (uint status in new[] { WarpLogicalMachineLayout.Completed, WarpLogicalMachineLayout.Faulted })
        foreach (int role in Enumerable.Range(0, 4))
        {
            uint[][] inputs = [[0xFFFFFFFF]];
            uint[] scalars = [0x80000000], state = layout.CreateInitialState(4, 100), arena = [0x7FC00001];
            state[WarpLogicalMachineLayout.StatusOffset] = status;
            uint[] before = (uint[])state.Clone();
            object owner = new(), authority = new();
            WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority,
                [SelectBank(role, inputs, scalars, state, arena)]);
            WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
            AssertDenied(() => InvokeQuantum(core, route, inputs, scalars, state, arena, 100), route == 2);
            CollectionAssert.AreEqual(before, state);
            WarpOrdinaryArrayRegistry.ReleaseHold(handle, owner, authority);
            InvokeQuantum(core, route, inputs, scalars, state, arena, 100, new(canceled: true));
            CollectionAssert.AreEqual(before, state);
            WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void GeneratedCancellationAndInvalidHeadersDisposeWithoutPartialPublication(int failure)
    {
        var layout = new WarpLogicalMachineLayout(NarrowKernel());
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        uint[][] inputs = [[11]];
        uint[] scalars = [3], state = layout.CreateInitialState(4, 100), arena = [0xBADC0FFE];
        if (failure == 1) { state[WarpLogicalMachineLayout.HeaderWords + WarpLogicalMachineLayout.FrameProgramCounterOffset] = uint.MaxValue; }
        if (failure == 2) { state[WarpLogicalMachineLayout.StatusOffset] = uint.MaxValue; }
        uint[] before = (uint[])state.Clone();
        object owner = new(), authority = new();
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [inputs[0], scalars, state, arena]);
        if (failure == 0)
        {
            Assert.ThrowsExactly<OperationCanceledException>(() => InvokeQuantum(core, 1, inputs, scalars, state, arena, 100, new(canceled: true)));
        }
        else { Assert.ThrowsExactly<InvalidOperationException>(() => InvokeQuantum(core, 1, inputs, scalars, state, arena, 100)); }
        CollectionAssert.AreEqual(before, state);
        Assert.AreEqual(0xBADC0FFEu, arena[0]);
        WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void QuantumYieldStepFaultAndCompletionReleaseAfterMachinePublication(int outcome)
    {
        var layout = new WarpLogicalMachineLayout(NarrowKernel());
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        uint[][] inputs = [[0xFEDCBA98]];
        uint[] scalars = [0x80000000], state = layout.CreateInitialState(4, outcome == 1 ? 1 : 100), arena = [0x7FC00001];
        object owner = new(), authority = new();
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [inputs[0], scalars, state, arena]);
        InvokeQuantum(core, 1, inputs, scalars, state, arena, outcome == 0 ? layout.MaximumBlockCost : 100);
        Assert.AreEqual(outcome == 0 ? WarpLogicalMachineLayout.Runnable : outcome == 1 ? WarpLogicalMachineLayout.Faulted : WarpLogicalMachineLayout.Completed,
            state[WarpLogicalMachineLayout.StatusOffset]);
        if (outcome == 1) { Assert.AreEqual(WarpLogicalMachineLayout.StepLimitFault, state[WarpLogicalMachineLayout.FaultKindOffset]); }
        if (outcome == 2) { Assert.AreEqual(inputs[0][0] ^ scalars[0], state[WarpLogicalMachineLayout.ResultOffset]); }
        Assert.AreEqual(0x7FC00001u, arena[0]);
        WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
        AssertDenied(() => InvokeQuantum(core, 1, inputs, scalars, state, arena, 100), reflection: false);
    }

    [TestMethod]
    public void GeneratedSourceBoundaryParkingReleasesWithoutChargingTheSourceStep()
    {
        WarpControlFlowKernel original = NarrowKernel();
        var metadata = new WarpLogicalExecutionMetadata([new(0, false, [1]), new(0, false, [1])], frameOwners: true);
        var layout = new WarpLogicalMachineLayout(new(original.Name + "/source-boundary", 1, 1,
            original.Blocks, null, original.Functions, metadata));
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        uint[][] inputs = [[7]];
        uint[] scalars = [11], state = layout.CreateInitialState(4, 100), arena = [0xFFFFFFFF];
        layout.SetSourceBoundaryMode(state, enabled: true);
        object owner = new(), authority = new();
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [inputs[0], scalars, state, arena]);
        InvokeQuantum(core, 1, inputs, scalars, state, arena, 100);
        Assert.AreEqual(WarpLogicalMachineLayout.Runnable, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(WarpLogicalMachineLayout.BeforeSourceBoundary, state[WarpLogicalMachineLayout.SourceBoundaryStateOffset]);
        Assert.AreEqual(100u, state[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
        Assert.AreEqual(0u, state[WarpLogicalMachineLayout.UsedOperationsLowOffset]);
        WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void PrivateProfileMethodInfoDeniesProtectedArraysAndKeepsWrapperMetadataDenial(int role)
    {
        WarpLogicalMachineLayout layout = PrivateFixture();
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        uint[][] inputs = [[0]];
        uint[] scalars = [0x80000000], state = layout.CreateInitialState(4, 100), arena = [0x7FC00001];
        uint[] before = (uint[])state.Clone();
        Assert.ThrowsExactly<InvalidOperationException>(() => core.ExecuteQuantum(inputs, scalars, 0, state, 4, 100));
        CollectionAssert.AreEqual(before, state);
        object owner = new(), authority = new();
        WarpOrdinaryArrayOwner handle = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, [SelectBank(role, inputs, scalars, state, arena)]);
        WarpOrdinaryArrayRegistry.Hold(handle, owner, authority);
        AssertDenied(() => InvokeQuantum(core, 2, inputs, scalars, state, arena, 100), reflection: true);
        CollectionAssert.AreEqual(before, state);
        WarpOrdinaryArrayRegistry.ReleaseHold(handle, owner, authority);
        // Unowned consistency execution is not an opaque Source/controller grant.
        InvokeQuantum(core, 2, inputs, scalars, state, arena, 100);
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(1u, state[WarpLogicalMachineLayout.ResultOffset]);
        WarpOrdinaryArrayRegistry.Quarantine(handle, owner, authority);
        uint[] completed = (uint[])state.Clone();
        AssertDenied(() => InvokeQuantum(core, 2, inputs, scalars, state, arena, 100), reflection: true);
        CollectionAssert.AreEqual(completed, state);
    }
}
