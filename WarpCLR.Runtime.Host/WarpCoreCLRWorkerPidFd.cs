using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCoreCLRWorkerPidFd : SafeHandleMinusOneIsInvalid
{
    internal WarpCoreCLRWorkerPidFd(int descriptor) : base(ownsHandle: true) => SetHandle(descriptor);

    protected override bool ReleaseHandle() => Close(handle.ToInt32()) == 0;

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int Close(int descriptor);
}
