using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WarpCLR.Runtime.Host.Native;

internal sealed class WarpNativeLibrary : SafeHandleZeroOrMinusOneIsInvalid
{
    private WarpNativeLibrary(IntPtr handle) : base(ownsHandle: true) => SetHandle(handle);

    public static WarpNativeLibrary Open(string? explicitPath, params string[] names)
    {
        if (explicitPath is not null)
        {
            if (!Path.IsPathFullyQualified(explicitPath))
            {
                throw new ArgumentException("An explicit native driver library path must be absolute.", nameof(explicitPath));
            }

            if (NativeLibrary.TryLoad(explicitPath, out IntPtr library)) { return new WarpNativeLibrary(library); }
        }
        else
        {
            foreach (string name in names)
            {
                if (NativeLibrary.TryLoad(name, Assembly.GetExecutingAssembly(),
                    DllImportSearchPath.SafeDirectories, out IntPtr library))
                {
                    return new WarpNativeLibrary(library);
                }
            }
        }

        throw new WarpHostException("WRPNATIVE1001", "The selected GPU driver library is unavailable; no CPU fallback is permitted.");
    }

    public T Export<T>(string name) where T : Delegate
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        if (!NativeLibrary.TryGetExport(handle, name, out IntPtr export))
        {
            throw new WarpHostException("WRPNATIVE1002", $"The GPU driver is missing the required export '{name}'.");
        }

        return Marshal.GetDelegateForFunctionPointer<T>(export);
    }

    protected override bool ReleaseHandle()
    {
        NativeLibrary.Free(handle);
        return true;
    }
}
