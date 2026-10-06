namespace WarpCLR.Runtime.Host;

internal enum WarpSourceArenaDecision
{
    ExistingAdmissionRequired,
    OpaqueSegmentReceiptRequired,
    SuccessfulFirstContainmentAndFixedRecoveryRequired,
    Excluded,
}
