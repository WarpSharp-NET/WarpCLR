using System.Diagnostics.CodeAnalysis;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;

namespace WarpCLR.Tests;

[TestClass]
[DoNotParallelize]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this fixture.")]
internal sealed class WarpPrivateHelperPhaseTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ActualPreparedGuestReturnPausesBeforeFreshControllerLoadAndAnyHelperEffect(bool largeQuantum)
    {
        var fixture = new WarpPrivateHelperPhaseFixture();
        int quantum = largeQuantum ? 4096 : fixture.Layout.MaximumBlockCost;
        uint[] arena = [0xA5A5A5A5, 0x7FC12345, 0x80000000]; uint[] state = fixture.ParkAfterGuestReturn(arena, quantum);
        int frame = WarpLogicalMachineLayout.HeaderWords + fixture.Layout.FrameWords;
        Assert.AreEqual((uint)fixture.Layout.GetBlockEntry(1, 1), state[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset]);
        Assert.AreEqual(98u, state[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
        Assert.AreEqual(0u, state[frame + WarpLogicalMachineLayout.FrameHeaderWords + 4]);
        Assert.AreEqual(0xFEEDC0DEu, state[frame + fixture.Layout.PrivateOffset]);
        Assert.AreEqual(0x80000000u, state[frame + fixture.Layout.PrivateOffset + 1]);
        Assert.AreEqual(0xA5A5A5A5u, arena[0]);
        uint[] before = (uint[])state.Clone(); uint[] arenaBefore = (uint[])arena.Clone();
        fixture.Invoke(state, arena, uint.MaxValue, quantum);
        CollectionAssert.AreEqual(before, state); CollectionAssert.AreEqual(arenaBefore, arena);
        fixture.Layout.AcknowledgePrivateHelperBoundary(state);
        uint[] acknowledged = (uint[])state.Clone();
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Layout.RequireAcknowledgedPrivateHelperInvocation(state, 0));
        fixture.Invoke(state, arena, 0, quantum);
        CollectionAssert.AreEqual(acknowledged, state); CollectionAssert.AreEqual(arenaBefore, arena);
        fixture.Layout.RequireAcknowledgedPrivateHelperInvocation(state, 0x81234567);
        Resume(fixture, state, arena, quantum);
        Assert.AreEqual(0x81234567u, state[WarpLogicalMachineLayout.ResultOffset]);
        Assert.AreEqual(0x7EEDC0DEu, state[WarpLogicalMachineLayout.ResultHighOffset]);
        Assert.AreEqual(97u, state[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
        Assert.AreEqual(0x81234567u, arena[0]); Assert.AreEqual(arenaBefore[1], arena[1]); Assert.AreEqual(arenaBefore[2], arena[2]);
        TestContext.WriteLine($"Controlled generated entry only: quantum={quantum}, method={fixture.Compiled.CompiledEntryPoint.MetadataToken}, module={fixture.Compiled.CompiledEntryPoint.Module.ModuleVersionId}, ir={WarpIrHash.Compute(fixture.Kernel)}; no child permit or registry issuance.");
    }

    [TestMethod]
    public void DisabledBoundaryModeKeepsZeroWordAsALegalConsistencyVectorWhileOrdinaryInvocationIsDenied()
    {
        var fixture = new WarpPrivateHelperPhaseFixture(); uint[] state = fixture.Layout.CreateInitialState(WarpPrivateHelperPhaseFixture.MaximumDepth, 100);
        uint[] arena = [0xA5A5A5A5]; uint[] before = (uint[])state.Clone();
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Compiled.ExecuteManagedQuantum([[0]], [0], 0, state,
            WarpPrivateHelperPhaseFixture.MaximumDepth, 4096, arena));
        CollectionAssert.AreEqual(before, state); Assert.AreEqual(0xA5A5A5A5u, arena[0]);
        fixture.Invoke(state, arena, 0, 4096);
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[0]);
        Assert.AreEqual(0u, state[WarpLogicalMachineLayout.ResultOffset]); Assert.AreEqual(0u, arena[0]);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void ChargedNonfirstGuestAndWrongSitePrivateBridgesFailStrictAdmission(int mutation) =>
        Assert.ThrowsExactly<ArgumentException>(() => new WarpPrivateHelperPhaseFixture(mutation: mutation));

    private static void Resume(WarpPrivateHelperPhaseFixture fixture, uint[] state, uint[] arena, int quantum)
    {
        for (int attempt = 0; attempt < 100 && state[0] == WarpLogicalMachineLayout.Runnable; attempt++)
        {
            if (state[WarpLogicalMachineLayout.SourceBoundaryStateOffset] == WarpLogicalMachineLayout.BeforeSourceBoundary)
            { WarpLogicalMachineLayout.AcknowledgeSourceBoundary(state); }
            fixture.Invoke(state, arena, 0x81234567, quantum);
        }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[0]);
    }
}
