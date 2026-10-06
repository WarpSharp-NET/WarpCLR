using System.Diagnostics.CodeAnalysis;
using WarpCLR.IR;

namespace WarpCLR.Tests;

[TestClass]
[DoNotParallelize]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this fixture.")]
internal sealed class WarpPrivateHelperReturnScopeTests
{
    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)] [DataRow(5)]
    [DataRow(6)] [DataRow(7)] [DataRow(8)] [DataRow(9)] [DataRow(10)] [DataRow(11)]
    [DataRow(12)] [DataRow(13)] [DataRow(14)] [DataRow(15)] [DataRow(16)] [DataRow(17)]
    public void MalformedActiveOuterScopeCannotCopyTupleOrRunCallerStores(int mutation)
    {
        var fixture = new WarpPrivateHelperReturnFenceFixture(nested: true);
        uint[] arena = [0xA5A5A5A5, 0x80000000, 0x7FC12345]; uint[] state = Activate(fixture, arena, nested: false);
        Mutate(fixture.Layout, state, mutation); AssertBlocked(fixture, state, arena);
    }

    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)]
    public void NestedReturnRequiresExactPureParentCallAndKeepsOuterScope(int mutation)
    {
        var fixture = new WarpPrivateHelperReturnFenceFixture(nested: true);
        uint[] arena = [0xA5A5A5A5, 0x80000000, 0x7FC12345]; uint[] state = Activate(fixture, arena, nested: true);
        int nestedFrame = WarpLogicalMachineLayout.HeaderWords + fixture.Layout.FrameWords * 3;
        int outer = nestedFrame - fixture.Layout.FrameWords;
        switch (mutation)
        {
            case 0: state[nestedFrame + WarpLogicalMachineLayout.FrameReturnValueOffset]++; break;
            case 1: state[nestedFrame + WarpLogicalMachineLayout.FrameReturnWordCountOffset]--; break;
            case 2: state[outer + WarpLogicalMachineLayout.FrameProgramCounterOffset] = (uint)fixture.Layout.GetBlockEntry(2, 0); break;
            case 3: state[nestedFrame + WarpLogicalMachineLayout.FrameFunctionOffset] = 4; break;
            case 4: state[nestedFrame + WarpLogicalMachineLayout.FrameActivationOffset] = state[outer + WarpLogicalMachineLayout.FrameActivationOffset]; break;
        }
        AssertBlocked(fixture, state, arena);
    }

    [TestMethod]
    public void ScopeIsRetainedAcrossHelperQuantaPublicationAndStaleReleaseWordThenClearedExactlyOnce()
    {
        var fixture = new WarpPrivateHelperReturnFenceFixture(nested: true);
        uint[] arena = [1, 0x80000000, 0x7FC12345]; uint[] state = Activate(fixture, arena, nested: false);
        WarpPrivateHelperReturnSite site = fixture.Layout.PrivateHelperReturnSites.RequireExactlyOne();
        uint[] scope = state.AsSpan(WarpLogicalMachineLayout.PrivateHelperScopeCallOffset, 4).ToArray();
        Assert.AreEqual((uint)site.CallProgramCounter + 1, scope[0]); Assert.AreEqual(2u, scope[1]);
        Assert.AreEqual(state[WarpLogicalMachineLayout.HeaderWords + fixture.Layout.FrameWords + WarpLogicalMachineLayout.FrameActivationOffset], scope[2]);
        Assert.AreEqual(state[WarpLogicalMachineLayout.HeaderWords + fixture.Layout.FrameWords * 2 + WarpLogicalMachineLayout.FrameActivationOffset], scope[3]);
        fixture.Invoke(state, arena, WarpPrivateHelperReturnFenceFixture.Controller, fixture.Layout.MaximumBlockCost);
        Assert.AreEqual(4u, state[WarpLogicalMachineLayout.DepthOffset]); CollectionAssert.AreEqual(scope, state.AsSpan(28, 4).ToArray());
        fixture.Invoke(state, arena, WarpPrivateHelperReturnFenceFixture.Controller, 4096);
        Assert.AreEqual(WarpLogicalMachineLayout.AwaitingRootRelease, state[15]); CollectionAssert.AreEqual(scope, state.AsSpan(28, 4).ToArray());
        fixture.Layout.AcknowledgePrivateHelperRelease(state); uint[] before = (uint[])state.Clone();
        fixture.Invoke(state, arena, 1, 4096); CollectionAssert.AreEqual(before, state);
        fixture.Invoke(state, arena, 0, 4096); CollectionAssert.AreEqual(new uint[4], state.AsSpan(28, 4).ToArray());
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[0]); Assert.AreEqual(99u, state[4]);
    }

    [TestMethod]
    public void DisabledBoundaryModeKeepsZeroWordAsAConsistencyVectorWithoutRecordingAScope()
    {
        var fixture = new WarpPrivateHelperReturnFenceFixture(); uint[] state = fixture.Layout.CreateInitialState(8, 100);
        uint[] arena = [1, 0x80000000, 0x7FC12345]; fixture.Invoke(state, arena, 0, 4096);
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[0]); Assert.AreEqual(0u, state[15]);
        CollectionAssert.AreEqual(new uint[4], state.AsSpan(28, 4).ToArray());
        Assert.AreEqual(0u, state[WarpLogicalMachineLayout.ResultOffset]); Assert.AreEqual(0x7FC12345u, state[WarpLogicalMachineLayout.ResultHighOffset]);
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Layout.AcknowledgePrivateHelperRelease(state));
    }

    private static uint[] Activate(WarpPrivateHelperReturnFenceFixture fixture, uint[] arena, bool nested)
    {
        int quantum = fixture.Layout.MaximumBlockCost; uint[] state = fixture.ParkBeforeHelper(arena, quantum);
        fixture.Layout.AcknowledgePrivateHelperBoundary(state); fixture.Invoke(state, arena, WarpPrivateHelperReturnFenceFixture.Controller, quantum);
        Assert.AreEqual(3u, state[6]); Assert.IsTrue(fixture.Layout.HasValidRuntimeHeader(state));
        if (nested) { fixture.Invoke(state, arena, WarpPrivateHelperReturnFenceFixture.Controller, quantum); Assert.AreEqual(4u, state[6]); }
        return state;
    }

    private static void AssertBlocked(WarpPrivateHelperReturnFenceFixture fixture, uint[] state, uint[] arena)
    {
        uint[] before = (uint[])state.Clone(); uint[] arenaBefore = (uint[])arena.Clone();
        Assert.IsFalse(fixture.Layout.HasValidRuntimeHeader(state));
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Layout.AcknowledgePrivateHelperRelease(state));
        foreach (int quantum in new int[] { fixture.Layout.MaximumBlockCost, 4096 })
        {
            fixture.Invoke(state, arena, WarpPrivateHelperReturnFenceFixture.Controller, quantum);
            CollectionAssert.AreEqual(before, state); CollectionAssert.AreEqual(arenaBefore, arena);
        }
    }

    private static void Mutate(WarpLogicalMachineLayout layout, uint[] state, int mutation)
    {
        int caller = WarpLogicalMachineLayout.HeaderWords + layout.FrameWords, outer = caller + layout.FrameWords;
        switch (mutation)
        {
            case 0: state[14] = 2; break;
            case 1: state[14] = 0; break;
            case 2: state[caller] = 0; break;
            case 3: state[caller + 1] = (uint)layout.GetBlockEntry(1, 0); break;
            case 4: state[outer + 2]++; break;
            case 5: state[outer + 3]--; break;
            case 6: state[28] = uint.MaxValue; break;
            case 7: state[29] = 0; break;
            case 8: state[29] = uint.MaxValue; break;
            case 9: state[30]++; break;
            case 10: state[31]++; break;
            case 11: state[outer + 4] = state[caller + 4]; break;
            case 12: state[outer] = 3; break;
            case 13: state.AsSpan(28, 4).Clear(); break;
            case 14: state[15] = WarpLogicalMachineLayout.AwaitingRootRelease; break;
            case 15: state[outer + 7] = 1; break;
            case 16: state[12] = 0; break;
            case 17: state[28] = 0; break;
        }
    }
}
