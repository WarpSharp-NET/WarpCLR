using System.Diagnostics.CodeAnalysis;
using WarpCLR.IR;

namespace WarpCLR.Tests;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this fixture.")]
internal sealed class WarpPrivateHelperReturnFenceGuardTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(9)]
    [DataRow(10)]
    [DataRow(11)]
    [DataRow(12)]
    [DataRow(13)]
    [DataRow(14)]
    [DataRow(15)]
    public void InvalidPublicationPcFrameNamespaceAndPhaseFailBeforeBankEffects(int mutation)
    {
        var fixture = new WarpPrivateHelperReturnFenceFixture(); uint[] arena = [1, 0x80000000, 0x7FC12345];
        uint[] state = fixture.ParkAfterHelper(arena, 4096);
        int frame = WarpLogicalMachineLayout.HeaderWords + fixture.Layout.FrameWords;
        Mutate(fixture.Layout, state, frame, mutation); uint[] before = (uint[])state.Clone(); uint[] arenaBefore = (uint[])arena.Clone();
        Assert.IsFalse(fixture.Layout.HasValidRuntimeHeader(state));
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Layout.AcknowledgePrivateHelperRelease(state));
        CollectionAssert.AreEqual(before, state);
        foreach (int quantum in new int[] { fixture.Layout.MaximumBlockCost, 4096 })
        { fixture.Invoke(state, arena, 0, quantum); CollectionAssert.AreEqual(before, state); CollectionAssert.AreEqual(arenaBefore, arena); }
    }

    [TestMethod]
    public void SourceHelperEntryAndDoubleReleaseAcknowledgementsCannotAcknowledgeTheReturnFence()
    {
        var fixture = new WarpPrivateHelperReturnFenceFixture(); uint[] state = fixture.ParkAfterHelper([1], 4096);
        uint[] before = (uint[])state.Clone();
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpLogicalMachineLayout.AcknowledgeSourceBoundary(state));
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Layout.AcknowledgePrivateHelperBoundary(state));
        CollectionAssert.AreEqual(before, state);
        fixture.Layout.AcknowledgePrivateHelperRelease(state); uint[] after = (uint[])state.Clone();
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Layout.AcknowledgePrivateHelperRelease(state));
        CollectionAssert.AreEqual(after, state);
    }

    private static void Mutate(WarpLogicalMachineLayout layout, uint[] state, int frame, int mutation)
    {
        switch (mutation)
        {
            case 0: state[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset] = (uint)layout.GetBlockEntry(1, 1); break;
            case 1: state[frame + WarpLogicalMachineLayout.FrameFunctionOffset] = 2; break;
            case 2: state[frame + WarpLogicalMachineLayout.FrameActivationOffset] = 0; break;
            case 3: state[WarpLogicalMachineLayout.OwnerContextOffset] = 0; break;
            case 4: state[WarpLogicalMachineLayout.SourceBoundaryModeOffset] = 0; break;
            case 5: state[WarpLogicalMachineLayout.StatusOffset] = WarpLogicalMachineLayout.Completed; break;
            case 6: state[frame + WarpLogicalMachineLayout.FramePrivateWordsOffset]++; break;
            case 7: state[frame + WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset] = 1; break;
            case 8: state[WarpLogicalMachineLayout.EscapedExceptionContextOffset] = 1; break;
            case 9: state[WarpLogicalMachineLayout.SourceBoundaryStateOffset] = 7; break;
            case 10: state[WarpLogicalMachineLayout.SourceBoundaryModeOffset] = 2; break;
            case 11: state[WarpLogicalMachineLayout.PrivateHelperScopeCallerDepthOffset]++; break;
            case 12: state[WarpLogicalMachineLayout.PrivateHelperScopeCallerActivationOffset]++; break;
            case 13: state[WarpLogicalMachineLayout.PrivateHelperScopeActivationOffset]++; break;
            case 14: state[frame + layout.FrameWords + WarpLogicalMachineLayout.FrameReturnValueOffset]++; break;
            case 15: state.AsSpan(WarpLogicalMachineLayout.PrivateHelperScopeCallOffset, 4).Clear(); break;
        }
    }
}
