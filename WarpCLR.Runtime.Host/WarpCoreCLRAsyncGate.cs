namespace WarpCLR.Runtime.Host;

internal sealed class WarpCoreCLRAsyncGate
{
    private readonly Lock sync = new();
    private readonly Queue<TaskCompletionSource> waiters = new();
    private bool held;

    internal async Task<Lease> AcquireAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (sync)
        {
            if (!held) { held = true; return new Lease(this); }
            if (waiters.Count >= 65536) { throw new WarpHostException("WRPCORECLR3004", "The word transaction waiting limit is exhausted."); }
            waiters.Enqueue(waiter);
        }
        using CancellationTokenRegistration registration = cancellationToken.Register(() => waiter.TrySetCanceled(cancellationToken));
        await waiter.Task.ConfigureAwait(false);
        return new Lease(this);
    }

    private void Release()
    {
        lock (sync)
        {
            while (waiters.TryDequeue(out TaskCompletionSource? waiter))
            { if (waiter.TrySetResult()) { return; } }
            held = false;
        }
    }

    internal sealed class Lease(WarpCoreCLRAsyncGate gate) : IDisposable
    {
        private int released;
        public void Dispose() { if (Interlocked.Exchange(ref released, 1) == 0) { gate.Release(); } }
    }
}
