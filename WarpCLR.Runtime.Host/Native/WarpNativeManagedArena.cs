using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

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
    private readonly WarpNativeOriginalArenaUse originalUse;
    private bool disposeRequested;
    private bool disposed;
    private bool quarantined;

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A failed constructor must preserve its exact original exception and retain the original bank whenever allocation retirement is unconfirmed.")]
    internal WarpNativeManagedArena(WarpNativeTarget target, WarpMachineMemoryOperations operations, uint[] initial,
        Action<Action> withContext, Action<Action> withCleanupContext, Action<WarpNativeManagedArena> released)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(withContext);
        ArgumentNullException.ThrowIfNull(withCleanupContext);
        ArgumentNullException.ThrowIfNull(released);
        originalUse = WarpNativeOriginalArenaUse.Acquire(initial);
        this.target = target;
        this.operations = operations;
        this.withContext = withContext;
        this.withCleanupContext = withCleanupContext;
        this.released = released;
        ownerIdentity = operations.ContextIdentity ?? operations;
        bool allocationAttempted = false;
        try
        {
            WordCount = checked((uint)initial.Length);
            bytes = checked((nuint)Math.Max(initial.Length, 1) * sizeof(uint));
            if ((ulong)bytes > target.GlobalMemoryBytes)
            {
                throw new WarpHostException("WRPNATIVE1005", "The persistent managed arena exceeds the concrete device's memory admission.");
            }
            allocationAttempted = true;
            pointer = operations.Allocate(bytes);
            if (pointer == 0) { throw new WarpHostException("WRPNATIVE1007", "The native allocator returned a null arena capability."); }
            using var pin = new WarpPinnedUInt32(initial);
            if (initial.Length != 0) { operations.Upload(pointer, pin.Pointer, bytes); }
        }
        catch (Exception failure)
        {
            DrainFailedConstruction(allocationAttempted, failure);
            throw;
        }
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

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "All retirement and release-callback failures must be collected without losing the first exception or releasing original-bank admission after unconfirmed retirement.")]
    private void ReleaseIfDrained()
    {
        if (!disposeRequested || leases.Count != 0 || disposed) { return; }
        ExceptionDispatchInfo? failure = null;
        try
        {
            operations.Free(pointer);
            originalUse.ReleaseAfterConfirmedRetirement();
        }
        catch (Exception exception)
        {
            failure = ExceptionDispatchInfo.Capture(exception);
            originalUse.RecordFailure(exception);
            QuarantineAfterFailedRetirement();
        }
        disposed = true;
        try { released(this); }
        catch (Exception exception)
        {
            originalUse.RecordFailure(exception);
            failure ??= ExceptionDispatchInfo.Capture(exception);
        }
        failure?.Throw();
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Constructor cleanup preserves the original failure and retains every cleanup failure and original bank when physical retirement cannot be confirmed.")]
    private void DrainFailedConstruction(bool allocationAttempted, Exception originalFailure)
    {
        originalUse.RecordFailure(originalFailure);
        try
        {
            if (pointer != 0)
            {
                operations.Free(pointer);
                originalUse.ReleaseAfterConfirmedRetirement();
            }
            else if (!allocationAttempted) { originalUse.ReleaseAfterConfirmedRetirement(); }
            else { QuarantineAfterFailedRetirement(); }
        }
        catch (Exception cleanupFailure)
        {
            originalUse.RecordFailure(cleanupFailure);
            QuarantineAfterFailedRetirement();
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A quarantine callback failure must be retained without masking the exact allocation or retirement failure; original bank admission remains live.")]
    private void QuarantineAfterFailedRetirement()
    {
        quarantined = true;
        try { operations.Quarantine(); }
        catch (Exception failure) { originalUse.RecordFailure(failure); }
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
