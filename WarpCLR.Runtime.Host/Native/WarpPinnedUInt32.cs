using System.Runtime.InteropServices;

namespace WarpCLR.Runtime.Host.Native;

internal sealed class WarpPinnedUInt32 : IDisposable
{
    private GCHandle pin;

    public WarpPinnedUInt32(uint[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data.Length == 0 ? new uint[1] : data;
        pin = GCHandle.Alloc(Data, GCHandleType.Pinned);
    }

    public uint[] Data { get; }
    public IntPtr Pointer => pin.AddrOfPinnedObject();

    public void Dispose()
    {
        if (pin.IsAllocated) { pin.Free(); }
    }
}
