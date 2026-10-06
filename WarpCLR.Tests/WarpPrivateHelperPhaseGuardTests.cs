using System.Diagnostics.CodeAnalysis;
using WarpCLR.IR;

namespace WarpCLR.Tests;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this fixture.")]
internal sealed class WarpPrivateHelperPhaseGuardTests
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
    public void WrongNodeModeStatusActivationAndWidthCannotAcknowledgeAHelperPhase(int mutation)
    {
        var fixture = new WarpPrivateHelperPhaseFixture(); uint[] arena = [1, 0x80000000, 0x7FC12345];
        uint[] state = fixture.ParkAfterGuestReturn(arena, 4096);
        int frame = WarpLogicalMachineLayout.HeaderWords + fixture.Layout.FrameWords;
        Mutate(fixture, state, frame, mutation);
        uint[] before = (uint[])state.Clone();
        Assert.IsFalse(fixture.Layout.HasValidRuntimeHeader(state));
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Layout.AcknowledgePrivateHelperBoundary(state));
        CollectionAssert.AreEqual(before, state);
        uint[] arenaBefore = (uint[])arena.Clone();
        foreach (uint phase in new uint[] { WarpLogicalMachineLayout.NeedsPrivateHelper, WarpLogicalMachineLayout.AcknowledgedPrivateHelper })
        {
            if (mutation != 6) { state[WarpLogicalMachineLayout.SourceBoundaryStateOffset] = phase; }
            uint[] guarded = (uint[])state.Clone();
            fixture.Invoke(state, arena, 0x81234567, fixture.Layout.MaximumBlockCost);
            fixture.Invoke(state, arena, 0x81234567, 4096);
            CollectionAssert.AreEqual(guarded, state); CollectionAssert.AreEqual(arenaBefore, arena);
        }
    }

    private static void Mutate(WarpPrivateHelperPhaseFixture fixture, uint[] state, int frame, int mutation)
    {
        switch (mutation)
        {
            case 0: state[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset] = (uint)fixture.Layout.GetBlockEntry(1, 2); break;
            case 1: state[WarpLogicalMachineLayout.SourceBoundaryModeOffset] = 0; break;
            case 2: state[0] = WarpLogicalMachineLayout.Completed; break;
            case 3: state[frame + WarpLogicalMachineLayout.FrameActivationOffset] = 0; break;
            case 4: state[frame + WarpLogicalMachineLayout.FramePrivateWordsOffset]++; break;
            case 5: state[frame] = 3; break;
            case 6: state[WarpLogicalMachineLayout.SourceBoundaryStateOffset] = 5; break;
            case 7: state[WarpLogicalMachineLayout.DepthOffset] = 0; break;
            case 8: state[frame + WarpLogicalMachineLayout.FrameActivationOffset] = state[frame - fixture.Layout.FrameWords + WarpLogicalMachineLayout.FrameActivationOffset]; break;
            case 9: state[frame + WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset] = 1; break;
            case 10: state[WarpLogicalMachineLayout.FrameStrideOffset]++; break;
            case 11: state[WarpLogicalMachineLayout.PrivateBaseOffset]++; break;
            case 12: state[WarpLogicalMachineLayout.DepthOffset] = uint.MaxValue; break;
            case 13: state[WarpLogicalMachineLayout.OwnerContextOffset] = 0; break;
            case 14: state[WarpLogicalMachineLayout.EscapedExceptionContextOffset] = 1; break;
        }
    }

    [TestMethod]
    public void SourceAcknowledgmentAndOldPrivateProfileCannotInterpretTheNewPhase()
    {
        var fixture = new WarpPrivateHelperPhaseFixture(); uint[] state = fixture.ParkAfterGuestReturn([1], 4096);
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpLogicalMachineLayout.AcknowledgeSourceBoundary(state));
        var old = new WarpPrivateHelperPhaseFixture(boundaries: false);
        Assert.AreEqual(WarpLogicalExecutionMetadata.PrivateControllerVersion, old.Kernel.Execution!.IdentityVersion, StringComparer.Ordinal);
        Assert.IsFalse(old.Layout.HasValidRuntimeHeader(state));
        fixture.Layout.AcknowledgePrivateHelperBoundary(state);
        uint[] before = (uint[])state.Clone();
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Layout.AcknowledgePrivateHelperBoundary(state));
        CollectionAssert.AreEqual(before, state);
    }
}
