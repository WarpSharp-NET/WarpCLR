namespace WarpCLR.Runtime.Host.Native;

internal sealed class WarpNativeManagedArena : IDisposable
{
    private readonly Lock gate = new();
    private readonly WarpMachineMemoryOperations operations;
    private readonly object ownerIdentity;
    private readonly WarpNativeTarget target;
    private readonly Action<Action> withContext;
    private readonly Action<Action> withCleanupContext;
    private readonly Action<WarpNativeManagedArena> released;
    private readonly HashSet<Lease> leases = [];
    private readonly ulong pointer;
    private readonly nuint bytes;
    private bool disposeRequested;
    private bool disposed;
    private bool quarantined;

    internal WarpNativeManagedArena(WarpNativeTarget target, WarpMachineMemoryOperations operations, uint[] initial,
        Action<Action> withContext, Action<Action> withCleanupContext, Action<WarpNativeManagedArena> released)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(withContext);
        ArgumentNullException.ThrowIfNull(withCleanupContext);
        ArgumentNullException.ThrowIfNull(released);
        this.target = target;
        this.operations = operations;
        this.withContext = withContext;
        this.withCleanupContext = withCleanupContext;
        this.released = released;
        ownerIdentity = operations.ContextIdentity ?? operations;
        WordCount = checked((uint)initial.Length);
        bytes = checked((nuint)Math.Max(initial.Length, 1) * sizeof(uint));
        if ((ulong)bytes > target.GlobalMemoryBytes)
        {
            throw new WarpHostException("WRPNATIVE1005", "The persistent managed arena exceeds the concrete device's memory admission.");
        }
        pointer = operations.Allocate(bytes);
        if (pointer == 0) { throw new WarpHostException("WRPNATIVE1007", "The native allocator returned a null arena capability."); }
        try
        {
            using var pin = new WarpPinnedUInt32(initial);
            if (initial.Length != 0) { operations.Upload(pointer, pin.Pointer, bytes); }
        }
        catch { operations.Free(pointer); throw; }
    }

    internal uint WordCount { get; }
    internal ulong ByteCount => (ulong)bytes;

    internal Lease Acquire(WarpNativeTarget requestedTarget, WarpMachineMemoryOperations requestedOperations)
    {
        lock (gate)
        {
            EnsureUsable();
            ObjectDisposedException.ThrowIf(disposeRequested, this);
            if (requestedTarget != target || !ReferenceEquals(ownerIdentity, requestedOperations.ContextIdentity ?? requestedOperations))
            {
                throw new WarpHostException("WRPNATIVE1004", "The managed arena belongs to a different native context or target.");
            }
            var lease = new Lease(this);
            leases.Add(lease);
            return lease;
        }
    }

    internal uint[] Snapshot()
    {
        uint[]? result = null;
        try
        {
            withContext(() =>
            {
                lock (gate)
                {
                    EnsureUsable();
                    ObjectDisposedException.ThrowIf(disposeRequested, this);
                    operations.Synchronize();
                    result = Capture();
                }
            });
        }
        catch (WarpHostException) { Quarantine(); operations.Quarantine(); throw; }
        return result!;
    }

    internal void Quarantine()
    {
        lock (gate) { quarantined = true; }
    }

    public void Dispose()
    {
        lock (gate) { if (disposed) { return; } }
        withCleanupContext(() =>
        {
            lock (gate)
            {
                if (disposed) { return; }
                disposeRequested = true;
                ReleaseIfDrained();
            }
        });
    }

    internal void CloseContext()
    {
        lock (gate) { if (disposed) { return; } }
        withCleanupContext(() =>
        {
            lock (gate)
            {
                if (disposed) { return; }
                disposeRequested = true;
                quarantined = true;
                foreach (Lease lease in leases) { lease.Invalidate(); }
                leases.Clear();
                ReleaseIfDrained();
            }
        });
    }

    private uint[] Capture()
    {
        var words = new uint[checked((int)WordCount)];
        using var pin = new WarpPinnedUInt32(words);
        if (words.Length != 0) { operations.Readback(pin.Pointer, pointer, bytes); }
        return words;
    }

    private void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (quarantined) { throw new WarpHostException("WRPNATIVE1006", "The persistent managed arena is quarantined."); }
    }

    private void Release(Lease lease) => withCleanupContext(() =>
    {
        lock (gate)
        {
            leases.Remove(lease);
            ReleaseIfDrained();
        }
    });

    private void ReleaseIfDrained()
    {
        if (!disposeRequested || leases.Count != 0 || disposed) { return; }
        try { operations.Free(pointer); }
        finally { disposed = true; released(this); }
    }

    internal sealed class Lease(WarpNativeManagedArena arena) : IDisposable
    {
        private bool disposed;

        internal uint WordCount => arena.WordCount;
        internal ulong ByteCount => arena.ByteCount;
        internal ulong Pointer
        {
            get
            {
                lock (arena.gate)
                {
                    ObjectDisposedException.ThrowIf(disposed, this);
                    arena.EnsureUsable();
                    return arena.pointer;
                }
            }
        }

        internal void Quarantine() => arena.Quarantine();
        internal void Invalidate() => disposed = true;

        internal void EnsureUsable()
        {
            lock (arena.gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                arena.EnsureUsable();
            }
        }

        public void Dispose()
        {
            lock (arena.gate)
            {
                if (disposed) { return; }
                disposed = true;
            }
            arena.Release(this);
        }
    }
}
