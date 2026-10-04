namespace WarpCLR.Compiler;

internal sealed class WarpPortableHeapRootHandle(Func<WarpPortableHeapServiceResult> read, Action release) : IDisposable
{
    private int disposed;

    public WarpPortableHeapReference Reference
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            WarpPortableHeapServiceResult result = read();
            if (result.Fault != 0)
            {
                throw new InvalidOperationException("The logical root lease is stale.");
            }
            return result.Reference;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            release();
        }
    }
}
