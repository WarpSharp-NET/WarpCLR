using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WarpCLR.Runtime.Host.Native;

internal sealed class WarpNativeBlock : SafeHandleZeroOrMinusOneIsInvalid
{
    public WarpNativeBlock(int size) : base(ownsHandle: true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        SetHandle(Marshal.AllocHGlobal(size));
        Size = size;
        Marshal.Copy(new byte[size], 0, handle, size);
    }

    public int Size { get; }
    public IntPtr Pointer => DangerousGetHandle();

    protected override bool ReleaseHandle()
    {
        Marshal.FreeHGlobal(handle);
        return true;
    }
}
