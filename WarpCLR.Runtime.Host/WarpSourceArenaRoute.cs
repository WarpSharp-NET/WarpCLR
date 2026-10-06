namespace WarpCLR.Runtime.Host;

internal enum WarpSourceArenaRoute
{
    Read8, Read16, Read32, Read64, Write8, Write16, Write32, Write64,
    Interlocked32, Interlocked64, VolatileRead32, VolatileRead64, VolatileWrite32, VolatileWrite64,
    MemoryFence, OrdinarySource, OrdinaryHelper, BatchState, BindInputs, UnbindInputs,
    Collection, ControllerTransition, HostObservation, HostMutation, NativeExecution,
    OwnedSource, OwnedPublication, NormalRelease, FixedStoppedCleanup,
}

