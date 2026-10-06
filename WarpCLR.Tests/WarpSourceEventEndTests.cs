using System.Diagnostics.CodeAnalysis;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this fixture.")]
internal sealed class WarpSourceEventEndTests
{
    [TestMethod]
    public void MatchingFirstInstructionEndStillRequiresOpaqueEndAndPublicationReceipts()
    {
        WarpSourceEventFixture fixture = WarpSourceEventFixture.Capture(); WarpSourceSegmentSnapshot origin = fixture.CandidateOrigin();
        WarpPortableSourceSegment segment = fixture.Map.Segments[0];
        WarpSourceSegmentSnapshot end = NextBoundary(fixture, origin);
        Assert.AreEqual(WarpSourceSegmentCandidateResult.OpaqueRegistryEndAndPublicationReceiptsRequired,
            WarpSourceSegmentEndContract.ValidateCandidate(fixture.Map, segment, end, WarpSourceEventFixture.MaximumDepth, 2,
                WarpPortableSourceSegmentEndKind.OriginalInstructionBoundary, 1000));
    }

    [TestMethod]
    public void CompletedStatusBoundaryNumbersAndWrongEndKindsCannotInventAFrontier()
    {
        WarpSourceEventFixture fixture = WarpSourceEventFixture.Capture(); WarpSourceSegmentSnapshot origin = fixture.CandidateOrigin();
        WarpPortableSourceSegment segment = fixture.Map.Segments[0];
        WarpSourceSegmentSnapshot end = NextBoundary(fixture, origin);
        foreach (WarpPortableSourceSegmentEndKind kind in new[] { WarpPortableSourceSegmentEndKind.SourceReturn,
            WarpPortableSourceSegmentEndKind.ManagedExceptionTerminal, (WarpPortableSourceSegmentEndKind)999 })
        { Assert.ThrowsExactly<InvalidOperationException>(() => WarpSourceSegmentEndContract.ValidateCandidate(fixture.Map, segment, end,
            WarpSourceEventFixture.MaximumDepth, 2, kind, 1000)); }
        WarpSourceSegmentSnapshot completed = WarpSourceEventFixture.Copy(end, state => state[WarpLogicalMachineLayout.StatusOffset] = WarpLogicalMachineLayout.Completed);
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpSourceSegmentEndContract.ValidateCandidate(fixture.Map, segment, completed,
            WarpSourceEventFixture.MaximumDepth, 2, WarpPortableSourceSegmentEndKind.OriginalInstructionBoundary, 1000));
    }

    [TestMethod]
    public void IncompleteBudgetOrUnboundPcCannotClaimTheEndOfOneOriginalInstruction()
    {
        WarpSourceEventFixture fixture = WarpSourceEventFixture.Capture(); WarpSourceSegmentSnapshot origin = fixture.CandidateOrigin();
        WarpPortableSourceSegment segment = fixture.Map.Segments[0]; WarpSourceSegmentSnapshot end = NextBoundary(fixture, origin);
        foreach (ulong before in new ulong[] { 0, 999, 1001, ulong.MaxValue })
        { Assert.ThrowsExactly<InvalidOperationException>(() => WarpSourceSegmentEndContract.ValidateCandidate(fixture.Map, segment, end,
            WarpSourceEventFixture.MaximumDepth, 2, WarpPortableSourceSegmentEndKind.OriginalInstructionBoundary, before)); }
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpSourceSegmentEndContract.ValidateCandidate(fixture.Map, segment, origin,
            WarpSourceEventFixture.MaximumDepth, 2, WarpPortableSourceSegmentEndKind.OriginalInstructionBoundary, 1001));
    }

    private static WarpSourceSegmentSnapshot NextBoundary(WarpSourceEventFixture fixture, WarpSourceSegmentSnapshot origin)
    {
        WarpPortableSourceSegmentFrontier frontier = fixture.Map.Segments[0].Frontiers.First(item => item.Kind == WarpPortableSourceSegmentEndKind.OriginalInstructionBoundary);
        return WarpSourceEventFixture.Copy(origin, state =>
        {
            state[WarpLogicalMachineLayout.HeaderWords + fixture.Map.Layout.FrameWords + WarpLogicalMachineLayout.FrameProgramCounterOffset] = (uint)frontier.ProgramCounter;
            state[WarpLogicalMachineLayout.RemainingStepsLowOffset]--;
        });
    }
}
