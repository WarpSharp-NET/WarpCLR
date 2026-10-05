namespace WarpCLR.Runtime.Host;

internal sealed record WarpCoreCLRWorkerTestHooks(byte Probe = 0)
{
    internal Action<int>? Started { get; init; }
    internal Action? PlanAdmitted { get; init; }
    internal Action<Guid>? JitEntering { get; init; }
    internal Action<int>? InheritedChild { get; init; }
    internal Action<WarpCoreCLRContainmentEvidence>? Killed { get; init; }
    internal Action<WarpCoreCLRCleanupEvidence>? CleanupMilestone { get; init; }
    internal Action? AfterContainmentSignal { get; init; }
    internal Action? BeforeResultPublication { get; init; }
    internal Action<IReadOnlyList<uint>, IReadOnlyList<uint>>? BeforeControllerResultPublication { get; init; }
    internal Action? BeforeBatchPublication { get; init; }
    internal Action? BeforeInputBindingPublication { get; init; }
    internal uint? GroupSignalProbeFlags { get; init; }
    internal Dictionary<string, string>? Environment { get; init; }
}
