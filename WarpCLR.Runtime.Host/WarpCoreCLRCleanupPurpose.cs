namespace WarpCLR.Runtime.Host;

internal enum WarpCoreCLRCleanupPurpose
{
    StoppedFault = 1,
    LeaseDrain = 2,
    AcquireFreeController = 3,
    ReleaseCapturedController = 4,
    CompleteCancellation = 5,
    DisposeContext = 6,
    StoppedController = 7,
    PublishControllerRelease = 8,
}

