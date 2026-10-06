namespace WarpCLR.Runtime.Host;

// This exhaustive route classifier returns REQUIREMENTS, never authorization.
// The actual registry must supply its private phase and opaque receipt.
internal static class WarpSourceArenaParticipation
{
    internal const string Version = "warp.source-arena-participation/all-widths-ipc-gc-controller-sticky-quarantine-requirements-only/0.1";

    internal static WarpSourceArenaDecision Classify(WarpSourceArenaPhase phase, WarpSourceArenaRoute route)
    {
        if (!Enum.IsDefined(phase) || !Enum.IsDefined(route)) { return WarpSourceArenaDecision.Excluded; }
        if (phase is WarpSourceArenaPhase.Quarantined or WarpSourceArenaPhase.TerminalQuarantine)
        {
            return phase == WarpSourceArenaPhase.Quarantined && route == WarpSourceArenaRoute.FixedStoppedCleanup ?
                WarpSourceArenaDecision.SuccessfulFirstContainmentAndFixedRecoveryRequired : WarpSourceArenaDecision.Excluded;
        }
        if (phase == WarpSourceArenaPhase.SegmentHeld)
        {
            return route is WarpSourceArenaRoute.OwnedSource or WarpSourceArenaRoute.OwnedPublication or WarpSourceArenaRoute.NormalRelease ?
                WarpSourceArenaDecision.OpaqueSegmentReceiptRequired : WarpSourceArenaDecision.Excluded;
        }
        return route is WarpSourceArenaRoute.OwnedSource or WarpSourceArenaRoute.OwnedPublication or
            WarpSourceArenaRoute.NormalRelease or WarpSourceArenaRoute.FixedStoppedCleanup ?
            WarpSourceArenaDecision.Excluded : WarpSourceArenaDecision.ExistingAdmissionRequired;
    }
}
