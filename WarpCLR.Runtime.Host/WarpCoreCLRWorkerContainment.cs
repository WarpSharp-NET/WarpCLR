using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WarpCLR.Runtime.Host;

internal static partial class WarpCoreCLRWorkerContainment
{
    internal const string SemanticId = "warp.coreclr-worker-containment/held-pidfd-sealed-census-first-outcome-complete-bounded-proc-stat-esrch-negative-group-sentinel-or-private-kill-on-close-job/0.4";

    internal static void Preflight(WarpCoreCLRWorkerTestHooks? probes)
    {
        if (OperatingSystem.IsWindows()) { return; }
        if (!OperatingSystem.IsLinux()) { throw new PlatformNotSupportedException("Bounded CoreCLR workers require Linux stable pidfd groups or Windows private jobs."); }
        WarpCoreCLRWorkerGroupCensus.ValidateProcDomain(Environment.ProcessId, GetProcessGroup(Environment.ProcessId));
        try
        {
            using WarpCoreCLRWorkerPidFd descriptor = OpenPidFd(Environment.ProcessId);
            descriptor.ValidateObservation();
            int result = Signal(descriptor, 0, probes?.GroupSignalProbeFlags ?? 4);
            int error = result == 0 ? 0 : Marshal.GetLastPInvokeError();
            if (result != 0 && error != 3)
            { throw new PlatformNotSupportedException($"Linux bounded worker containment requires held pidfd process-group signals (Linux6.9+); preflight errno={error}."); }
        }
        catch (EntryPointNotFoundException error)
        { throw new PlatformNotSupportedException("The actual libc does not expose required stable pidfd containment.", error); }
    }

    internal static WarpCoreCLRWorkerContainmentLease Attach(Process process)
    {
        if (OperatingSystem.IsLinux())
        {
            if (process.HasExited) { throw new InvalidDataException("The worker exited before stable process ownership was captured."); }
            WarpCoreCLRWorkerPidFd descriptor = OpenPidFd(process.Id);
            if (process.HasExited) { descriptor.Dispose(); throw new InvalidDataException("The original worker exited while stable process ownership was captured."); }
            return new(process.Id, descriptor, null);
        }
        if (!OperatingSystem.IsWindows()) { throw new PlatformNotSupportedException("Bounded CoreCLR workers require Linux stable pidfd groups or Windows jobs."); }
        SafeFileHandle job = CreateJob(IntPtr.Zero, IntPtr.Zero);
        if (job.IsInvalid) { job.Dispose(); throw new Win32Exception(Marshal.GetLastPInvokeError()); }
        var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } };
        if (!SetJobLimits(job, 9, in limits, (uint)Marshal.SizeOf<ExtendedLimits>()) || !AssignJob(job, process.SafeHandle))
        {
            int error = Marshal.GetLastPInvokeError(); job.Dispose(); throw new Win32Exception(error);
        }
        return new(process.Id, null, job);
    }

    internal static WarpCoreCLRWorkerPidFd OpenPidFd(int process)
    {
        int descriptor = PidFdOpen(process, 0);
        if (descriptor < 0) { throw new PlatformNotSupportedException($"Stable pidfd process ownership is unavailable; errno={Marshal.GetLastPInvokeError()}."); }
        return new(descriptor);
    }

    internal static int Signal(WarpCoreCLRWorkerPidFd descriptor, int signal, uint flags) =>
        PidFdSendSignal(descriptor, signal, IntPtr.Zero, flags);

    internal static int GetProcessGroup(int process) => OperatingSystem.IsLinux() ? ProcessGroup(process) : 0;

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libc", EntryPoint = "getpgid", SetLastError = true)]
    private static partial int ProcessGroup(int processId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libc", EntryPoint = "pidfd_open", SetLastError = true)]
    private static partial int PidFdOpen(int process, uint flags);
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libc", EntryPoint = "pidfd_send_signal", SetLastError = true)]
    private static partial int PidFdSendSignal(WarpCoreCLRWorkerPidFd descriptor, int signal, IntPtr information, uint flags);
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true)]
    private static partial SafeFileHandle CreateJob(IntPtr attributes, IntPtr name);
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("kernel32.dll", EntryPoint = "AssignProcessToJobObject", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignJob(SafeFileHandle job, SafeProcessHandle process);
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("kernel32.dll", EntryPoint = "SetInformationJobObject", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetJobLimits(SafeFileHandle job, int informationClass, in ExtendedLimits information, uint length);

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        internal long ProcessTime, JobTime;
        internal uint Flags;
        internal nuint MinimumWorkingSet, MaximumWorkingSet;
        internal uint ActiveProcesses;
        internal nuint Affinity;
        internal uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        internal ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        internal BasicLimits Basic;
        internal IoCounters Io;
        internal nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
}
