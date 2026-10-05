namespace WarpCLR.Runtime.Host;

internal sealed record WarpCoreCLRContainmentEvidence(int ParentProcess, int ParentGroup, int ChildProcess,
    int ChildGroup, int RegisteredPrivateGroup, int Signal, bool GroupSignalled, int SignalResult, int SignalError = 0, bool StableHandle = false, bool LeaderExited = false);
