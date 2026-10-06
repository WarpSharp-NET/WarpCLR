using System.Runtime.InteropServices;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host.Native;

internal sealed class WarpPinnedUInt32 : IDisposable
{
    private readonly WarpOrdinaryArrayAdmission admission;
    private GCHandle pin;
    private int disposed;

    public WarpPinnedUInt32(uint[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data.Length == 0 ? new uint[1] : data;
        admission = WarpOrdinaryArrayAdmission.Acquire([], [], Data, []);
        try
        {
            pin = GCHandle.Alloc(Data, GCHandleType.Pinned);
        }
        catch
        {
            admission.Dispose();
            throw;
        }
    }

    public uint[] Data { get; }
    public IntPtr Pointer
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            return pin.AddrOfPinnedObject();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) { return; }
        try { if (pin.IsAllocated) { pin.Free(); } }
        finally { admission.Dispose(); }
    }
}
