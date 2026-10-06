using System.Diagnostics.CodeAnalysis;
using WarpCLR.IR;

namespace WarpCLR.Tests;

[TestClass]
[DoNotParallelize]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this fixture.")]
internal sealed class WarpPrivateHelperReturnFenceTests
{
    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(false, false, true)]
    [DataRow(false, true, false)]
    [DataRow(false, true, true)]
    [DataRow(true, false, false)]
    [DataRow(true, false, true)]
    [DataRow(true, true, false)]
    [DataRow(true, true, true)]
    public void ExactCallerTupleFencesBeforeAnyCallerStoreGuestCallOrTerminal(bool nested, bool guestSuccessor, bool largeQuantum)
    {
        var fixture = new WarpPrivateHelperReturnFenceFixture(nested, guestSuccessor);
        int quantum = largeQuantum ? 4096 : fixture.Layout.MaximumBlockCost;
        uint[] arena = [0xA5A5A5A5, 0x80000000, 0x7FC12345]; uint[] state = fixture.ParkAfterHelper(arena, quantum);
        WarpPrivateHelperReturnSite site = fixture.Layout.PrivateHelperReturnSites.RequireExactlyOne();
        int frame = WarpLogicalMachineLayout.HeaderWords + fixture.Layout.FrameWords;
        Assert.AreEqual(2u, state[WarpLogicalMachineLayout.DepthOffset]); Assert.AreEqual(WarpLogicalMachineLayout.Runnable, state[0]);
        Assert.AreEqual((uint)site.Continuation, state[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset]);
        CollectionAssert.AreEqual(new uint[] { WarpPrivateHelperReturnFenceFixture.Controller, 0x7FC12345, 0x80000000, uint.MaxValue },
            state.AsSpan(frame + WarpLogicalMachineLayout.FrameHeaderWords + site.ResultValue, site.ResultWordCount).ToArray());
        Assert.AreEqual(0xA5A5A5A5u, state[frame + fixture.Layout.PrivateOffset]);
        Assert.AreEqual(0xA5A5A5A5u, state[frame + fixture.Layout.PrivateOffset + 1]);
        Assert.AreEqual(0u, state[WarpLogicalMachineLayout.ResultOffset]); Assert.AreEqual(99u, state[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
        CollectionAssert.AreEqual(new uint[] { WarpPrivateHelperReturnFenceFixture.Controller, 0x80000000, 0x7FC12345 }, arena);
        uint[] before = (uint[])state.Clone(); uint[] arenaBefore = (uint[])arena.Clone();
        foreach (uint word in new uint[] { 0, WarpPrivateHelperReturnFenceFixture.Controller, uint.MaxValue })
        { fixture.Invoke(state, arena, word, quantum); CollectionAssert.AreEqual(before, state); CollectionAssert.AreEqual(arenaBefore, arena); }
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Compiled.ExecuteManagedQuantum([[0]], [0], 0, state,
            WarpPrivateHelperReturnFenceFixture.MaximumDepth, quantum, arena));
        CollectionAssert.AreEqual(before, state); CollectionAssert.AreEqual(arenaBefore, arena);
        fixture.Layout.AcknowledgePrivateHelperRelease(state); uint[] acknowledged = (uint[])state.Clone();
        fixture.Invoke(state, arena, WarpPrivateHelperReturnFenceFixture.Controller, quantum);
        CollectionAssert.AreEqual(acknowledged, state); CollectionAssert.AreEqual(arenaBefore, arena);
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Layout.RequireAcknowledgedPrivateHelperRelease(state, 1));
        fixture.Layout.RequireAcknowledgedPrivateHelperRelease(state, 0);
        ResumeAfterControlledRelease(fixture, state, arena, quantum);
        Assert.AreEqual(WarpPrivateHelperReturnFenceFixture.Controller, state[WarpLogicalMachineLayout.ResultOffset]);
        Assert.AreEqual(0x7FC12345u, state[WarpLogicalMachineLayout.ResultHighOffset]);
        Assert.AreEqual(guestSuccessor ? 98u : 99u, state[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
        Assert.AreEqual(guestSuccessor ? 0xBAD0C0DEu : 0x80000000u, arena[1]); Assert.AreEqual(0x7FC12345u, arena[2]);
    }

    [TestMethod]
    public void NestedPureHelperRemainsResumableAcrossSmallQuantaAndOnlyOuterReturnFences()
    {
        var fixture = new WarpPrivateHelperReturnFenceFixture(nested: true);
        int quantum = fixture.Layout.MaximumBlockCost; uint[] arena = [1, 0x80000000, 0x7FC12345];
        uint[] state = fixture.ParkBeforeHelper(arena, quantum); uint owner = state[WarpLogicalMachineLayout.OwnerContextOffset];
        fixture.Layout.AcknowledgePrivateHelperBoundary(state);
        fixture.Invoke(state, arena, WarpPrivateHelperReturnFenceFixture.Controller, quantum);
        Assert.AreEqual(3u, state[WarpLogicalMachineLayout.DepthOffset]); Assert.AreEqual(0u, state[15]);
        Assert.AreEqual(99u, state[4]); Assert.AreEqual(1u, arena[0]);
        fixture.Invoke(state, arena, WarpPrivateHelperReturnFenceFixture.Controller, quantum);
        Assert.AreEqual(4u, state[WarpLogicalMachineLayout.DepthOffset]); Assert.AreEqual(0u, state[15]);
        Assert.AreEqual(99u, state[4]); Assert.AreEqual(owner, state[WarpLogicalMachineLayout.OwnerContextOffset]);
        fixture.Invoke(state, arena, WarpPrivateHelperReturnFenceFixture.Controller, quantum);
        Assert.AreEqual(2u, state[WarpLogicalMachineLayout.DepthOffset]);
        Assert.AreEqual(WarpLogicalMachineLayout.AwaitingRootRelease, state[15]); Assert.AreEqual(99u, state[4]);
        Assert.AreEqual(WarpPrivateHelperReturnFenceFixture.Controller, arena[0]);
    }

    private static void ResumeAfterControlledRelease(WarpPrivateHelperReturnFenceFixture fixture, uint[] state, uint[] arena, int quantum)
    {
        for (int attempt = 0; attempt < 100 && state[0] == WarpLogicalMachineLayout.Runnable; attempt++)
        {
            if (state[WarpLogicalMachineLayout.SourceBoundaryStateOffset] == WarpLogicalMachineLayout.BeforeSourceBoundary)
            { WarpLogicalMachineLayout.AcknowledgeSourceBoundary(state); }
            fixture.Invoke(state, arena, 0, quantum);
        }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[0]);
    }
}
