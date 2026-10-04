namespace WarpCLR.Compiler;

internal sealed class WarpPortableHeapContext : IDisposable
{
    private static long nextContext;
    private readonly Lock gate = new();
    private readonly uint[] arena;
    private bool disposed;

    public WarpPortableHeapContext(WarpPortableHeapSchema schema, uint payloadWords = 4096, uint maximumObjects = 128,
        uint maximumRoots = 64, uint maximumWorkers = 64, uint quotaWords = 4096)
    {
        ArgumentNullException.ThrowIfNull(schema);
        long token = Interlocked.Increment(ref nextContext);
        if (token <= 0 || token > uint.MaxValue)
        {
            throw new InvalidOperationException("The nonreusing logical context namespace is exhausted.");
        }
        arena = schema.CreateArena((uint)token, payloadWords, maximumObjects, maximumRoots, maximumWorkers, quotaWords);
    }

    public WarpPortableHeapServiceResult Execute(Func<uint[], uint> service)
    {
        ArgumentNullException.ThrowIfNull(service);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            uint fault = service(arena);
            return Snapshot(arena, fault);
        }
    }

    public WarpPortableHeapRootHandle AcquireRoot(WarpPortableHeapReference reference)
    {
        WarpPortableHeapServiceResult result = Execute(words => WarpPortableHeapServices.AcquireRoot(words,
            reference.Context, reference.Slot, reference.Generation, WarpPortableHeapLayout.StrongRoot, 0, 0, 0));
        if (result.Fault != 0)
        {
            throw new InvalidOperationException("A logical strong root could not be acquired.");
        }
        return new WarpPortableHeapRootHandle(() => Execute(words => WarpPortableHeapServices.ReadRoot(words, result.Word0, result.Word1)),
            () => ReleaseRoot(result.Word0, result.Word1));
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (!disposed)
            {
                disposed = true;
                Array.Clear(arena);
            }
        }
    }

    private void ReleaseRoot(uint root, uint generation)
    {
        lock (gate)
        {
            if (!disposed)
            {
                WarpPortableHeapServices.ReleaseRoot(arena, root, generation);
            }
        }
    }

    private static WarpPortableHeapServiceResult Snapshot(uint[] words, uint fault) => new(fault,
        words[WarpPortableHeapLayout.Operation], words[WarpPortableHeapLayout.Argument0], words[WarpPortableHeapLayout.Argument1],
        words[WarpPortableHeapLayout.Result], words[WarpPortableHeapLayout.Result + 1], words[WarpPortableHeapLayout.Result + 2],
        words[WarpPortableHeapLayout.Result + 3], words[WarpPortableHeapLayout.Result + 4], words[WarpPortableHeapLayout.Result + 5]);
}
