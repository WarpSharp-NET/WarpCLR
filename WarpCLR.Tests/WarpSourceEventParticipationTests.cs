using System.Diagnostics.CodeAnalysis;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this fixture.")]
internal sealed class WarpSourceEventParticipationTests
{
    [TestMethod]
    [DataRow(33)]
    [DataRow(131)]
    public void HeldSegmentExcludesEveryOrdinaryWidthAndIpcGcControllerRoute(int logicalParticipants)
    {
        // Policy coverage over participant counts is not a runtime census proof.
        foreach (int participant in Enumerable.Range(0, logicalParticipants))
        {
            foreach (WarpSourceArenaRoute route in Enum.GetValues<WarpSourceArenaRoute>())
            {
                WarpSourceArenaDecision expected = route is WarpSourceArenaRoute.OwnedSource or WarpSourceArenaRoute.OwnedPublication or WarpSourceArenaRoute.NormalRelease ?
                    WarpSourceArenaDecision.OpaqueSegmentReceiptRequired : WarpSourceArenaDecision.Excluded;
                Assert.AreEqual(expected, WarpSourceArenaParticipation.Classify(WarpSourceArenaPhase.SegmentHeld, route), $"Participant {participant}, {route}");
            }
        }
    }

    [TestMethod]
    public void CleanupCannotReopenPermanentQuarantineOrAuthorizeOrdinarySource()
    {
        foreach (WarpSourceArenaRoute route in Enum.GetValues<WarpSourceArenaRoute>())
        {
            Assert.AreEqual(WarpSourceArenaDecision.Excluded, WarpSourceArenaParticipation.Classify(WarpSourceArenaPhase.TerminalQuarantine, route));
            Assert.AreEqual(route == WarpSourceArenaRoute.FixedStoppedCleanup ? WarpSourceArenaDecision.SuccessfulFirstContainmentAndFixedRecoveryRequired : WarpSourceArenaDecision.Excluded,
                WarpSourceArenaParticipation.Classify(WarpSourceArenaPhase.Quarantined, route));
        }
    }

    [TestMethod]
    public void UnknownPhasesRoutesAndIdlePrivateInvocationsFailClosed()
    {
        Assert.AreEqual(WarpSourceArenaDecision.Excluded, WarpSourceArenaParticipation.Classify((WarpSourceArenaPhase)999, WarpSourceArenaRoute.Read64));
        Assert.AreEqual(WarpSourceArenaDecision.Excluded, WarpSourceArenaParticipation.Classify(WarpSourceArenaPhase.Idle, (WarpSourceArenaRoute)999));
        foreach (WarpSourceArenaRoute route in new[] { WarpSourceArenaRoute.OwnedSource, WarpSourceArenaRoute.OwnedPublication,
            WarpSourceArenaRoute.NormalRelease, WarpSourceArenaRoute.FixedStoppedCleanup })
        { Assert.AreEqual(WarpSourceArenaDecision.Excluded, WarpSourceArenaParticipation.Classify(WarpSourceArenaPhase.Idle, route)); }
        Assert.AreEqual(WarpSourceArenaDecision.ExistingAdmissionRequired, WarpSourceArenaParticipation.Classify(WarpSourceArenaPhase.Idle, WarpSourceArenaRoute.Read8));
    }
}
