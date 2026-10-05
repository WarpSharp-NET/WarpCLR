using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCoreCLRWorkerPidFd
{
    internal void ValidateObservation()
    {
        var descriptor = new PollDescriptor { File = DangerousGetHandle().ToInt32(), Events = 1 };
        if (Poll(ref descriptor, 1, 0) < 0 || (descriptor.Returned & 0x28) != 0)
        { throw new PlatformNotSupportedException("The held pidfd exit observation facility is unavailable."); }
    }

    internal void WaitStopped(WarpCoreCLRWorkerCleanupAttempt attempt)
    {
        bool retained = false;
        try
        {
            DangerousAddRef(ref retained);
            var descriptor = new PollDescriptor { File = DangerousGetHandle().ToInt32(), Events = 1 };
            while (attempt.Remaining > TimeSpan.Zero)
            {
                int milliseconds = (int)Math.Clamp(Math.Ceiling(attempt.Remaining.TotalMilliseconds), 0, int.MaxValue);
                int result = Poll(ref descriptor, 1, milliseconds);
                if (result > 0 && (descriptor.Returned & 0x11) != 0) { return; }
                if (result < 0 && Marshal.GetLastPInvokeError() == 4) { continue; }
                if (result < 0) { throw new Win32Exception(Marshal.GetLastPInvokeError()); }
                if (result > 0) { throw new IOException("The held pidfd terminal acknowledgement was invalid."); }
            }
            throw new TimeoutException("The held CoreCLR process identity did not report exit within the shared cleanup quota.");
        }
        finally { if (retained) { DangerousRelease(); } }
    }

    internal bool ObserveStopped()
    {
        bool retained = false;
        try
        {
            DangerousAddRef(ref retained);
            var descriptor = new PollDescriptor { File = DangerousGetHandle().ToInt32(), Events = 1 };
            int result = Poll(ref descriptor, 1, 0);
            if (result < 0 && Marshal.GetLastPInvokeError() == 4) { return false; }
            if (result < 0) { throw new Win32Exception(Marshal.GetLastPInvokeError()); }
            if (result == 0) { return false; }
            if ((descriptor.Returned & 0x11) == 0) { throw new IOException("The held pidfd terminal observation was invalid."); }
            return true;
        }
        finally { if (retained) { DangerousRelease(); } }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PollDescriptor
    {
        internal int File;
        internal short Events;
        internal short Returned;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static partial int Poll(ref PollDescriptor descriptor, nuint count, int milliseconds);
}
