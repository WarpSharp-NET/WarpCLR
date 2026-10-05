using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Collections.Concurrent;

namespace WarpCLR.Runtime.Host;

internal sealed class WarpCoreCLRWorkerContainmentLease : IDisposable
{
    private readonly int child;
    private readonly WarpCoreCLRWorkerPidFd? pidfd;
    private readonly SafeFileHandle? job;
    private int privateGroup;
    private readonly ConcurrentDictionary<int, WarpCoreCLRWorkerPidFd> admittedDescendants = [];
    private bool stoppedCensusSealed;

    internal WarpCoreCLRWorkerContainmentLease(int child, WarpCoreCLRWorkerPidFd? pidfd, SafeFileHandle? job)
    {
        this.child = child; this.pidfd = pidfd; this.job = job;
    }

    internal void RegisterPrivateGroup(Process process)
    {
        if (OperatingSystem.IsLinux())
        {
            int group = WarpCoreCLRWorkerContainment.GetProcessGroup(child);
            if (process.Id != child || group != child || group <= 1 || group == WarpCoreCLRWorkerContainment.GetProcessGroup(Environment.ProcessId))
            { throw new InvalidDataException("The authenticated worker did not create its own private Unix process group."); }
            int result = WarpCoreCLRWorkerContainment.Signal(pidfd!, 0, 4);
            if (result != 0) { throw new Win32Exception(Marshal.GetLastPInvokeError()); }
            privateGroup = group;
        }
    }

    internal void Kill(Process process, WarpCoreCLRWorkerTestHooks? probes)
    {
        if (process.Id != child) { throw new InvalidDataException("Containment cannot signal a different process object."); }
        if (!OperatingSystem.IsLinux()) { job!.Dispose(); return; }
        int parent = WarpCoreCLRWorkerContainment.GetProcessGroup(Environment.ProcessId);
        int current = WarpCoreCLRWorkerContainment.GetProcessGroup(child);
        bool group = privateGroup == child && child > 1 && parent != child && Environment.ProcessId != child;
        if (privateGroup != 0 && !group) { throw new InvalidDataException("The held worker group now conflicts with the parent containment domain."); }
        bool leaderExited = process.HasExited;
        int result = WarpCoreCLRWorkerContainment.Signal(pidfd!, 9, group ? 4u : 0u);
        int error = result == 0 ? 0 : Marshal.GetLastPInvokeError();
        probes?.Killed?.Invoke(new(Environment.ProcessId, parent, child, current, privateGroup, 9, group, result,
            error, true, leaderExited));
        if (result != 0 && error != 3) { throw new Win32Exception(error); }
    }

    internal void RegisterDescendant(int process)
    {
        if (!OperatingSystem.IsLinux()) { return; }
        if (privateGroup != child || WarpCoreCLRWorkerContainment.GetProcessGroup(process) != child)
        { throw new InvalidDataException("The authenticated inherited-process receipt is outside the held private group."); }
        WarpCoreCLRWorkerPidFd descriptor = WarpCoreCLRWorkerContainment.OpenPidFd(process);
        if (WarpCoreCLRWorkerContainment.GetProcessGroup(process) != child)
        { descriptor.Dispose(); throw new InvalidDataException("The inherited-process identity changed during stable admission."); }
        if (!admittedDescendants.TryAdd(process, descriptor))
        { descriptor.Dispose(); throw new InvalidDataException("The inherited-process identity was already admitted."); }
    }

    internal void SealStoppedCensus(WarpCoreCLRWorkerCleanupAttempt attempt)
    {
        if (OperatingSystem.IsLinux() && privateGroup != 0)
        {
            HashSet<int> declared = [child, .. admittedDescendants.Keys];
            WarpCoreCLRWorkerGroupCensus.ValidateDeclared(privateGroup, declared, attempt);
        }
        stoppedCensusSealed = true;
    }

    internal void WaitStopped(WarpCoreCLRWorkerCleanupAttempt attempt)
    {
        if (!OperatingSystem.IsLinux()) { return; }
        if (!stoppedCensusSealed) { throw new InvalidOperationException("The stopped held-handle census was not sealed within the cleanup quota."); }
        if (attempt.Remaining <= TimeSpan.Zero) { ObserveLateStopped(); return; }
        try
        {
            pidfd!.WaitStopped(attempt);
            foreach (WarpCoreCLRWorkerPidFd descendant in admittedDescendants.Values) { descendant.WaitStopped(attempt); }
            if (privateGroup != 0) { WarpCoreCLRWorkerGroupCensus.WaitStopped(privateGroup, attempt); }
        }
        catch (TimeoutException) when (attempt.Remaining <= TimeSpan.Zero) { ObserveLateStopped(); }
    }

    private void ObserveLateStopped()
    {
        // This only retires positively terminal held identities. It cannot turn the shared failed deadline into success.
        if (!pidfd!.ObserveStopped() || admittedDescendants.Values.Any(descendant => !descendant.ObserveStopped()))
        { throw new TimeoutException("The complete sealed held-handle census has not positively reported terminal state."); }
    }

    public void Dispose()
    {
        foreach (WarpCoreCLRWorkerPidFd descendant in admittedDescendants.Values) { descendant.Dispose(); }
        pidfd?.Dispose(); job?.Dispose();
    }
}
