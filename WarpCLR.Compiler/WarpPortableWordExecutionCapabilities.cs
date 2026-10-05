namespace WarpCLR.Compiler;

internal sealed record WarpPortableWordExecutionCapabilities(bool FrameOwners = false, bool RuntimeStateAccess = false,
    bool NonlocalStateDispatch = false, bool ManagedExceptionTermination = false, bool LogicalWorkerAccess = false,
    bool PrivateController = false);
