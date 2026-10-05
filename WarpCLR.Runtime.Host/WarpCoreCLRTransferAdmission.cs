namespace WarpCLR.Runtime.Host;

internal static class WarpCoreCLRTransferAdmission
{
    // Global conservative allowance for transient IPC copies and retained immutable inputs.
    internal const long MaximumBytes = 1024L * 1024 * 1024;
    private static readonly Lock Sync = new();
    private static readonly Queue<Request> Waiting = new();
    private static long reserved;

    internal static async Task<Lease> AcquireAsync(long bytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes <= 0 || bytes > MaximumBytes) { throw new WarpHostException("WRPCORECLR3004", "IPC working storage exceeds its explicit global admission."); }
        var completion = new TaskCompletionSource<Lease>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (Sync)
        {
            if (Waiting.Count == 0 && reserved + bytes <= MaximumBytes)
            { reserved += bytes; return new Lease(bytes); }
            if (Waiting.Count >= 65536) { throw new WarpHostException("WRPCORECLR3004", "IPC storage waiting admission is exhausted."); }
            Waiting.Enqueue(new(bytes, completion));
        }
        using CancellationTokenRegistration registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        return await completion.Task.ConfigureAwait(false);
    }

    private static void Release(long bytes)
    {
        lock (Sync)
        {
            reserved -= bytes;
            while (Waiting.TryPeek(out Request? request))
            {
                if (request.Completion.Task.IsCompleted) { Waiting.Dequeue(); continue; }
                if (reserved + request.Bytes > MaximumBytes) { break; }
                Waiting.Dequeue();
                Lease? lease = new(request.Bytes);
                try
                {
                    if (request.Completion.TrySetResult(lease)) { reserved += request.Bytes; lease = null; }
                    else { lease.CancelUnassigned(); }
                }
                finally { lease?.Dispose(); }
            }
        }
    }

    internal static long ReservedBytes { get { lock (Sync) { return reserved; } } }
    private sealed record Request(long Bytes, TaskCompletionSource<Lease> Completion);

    internal sealed class Lease(long bytes) : IDisposable
    {
        private readonly Lock sync = new();
        private long remaining = bytes;
        internal void CancelUnassigned() { lock (sync) { remaining = 0; } }
        internal void Retain(long retainedBytes)
        {
            long original;
            lock (sync)
            {
                original = remaining;
                if (retainedBytes <= 0 || retainedBytes > original) { throw new InvalidOperationException("IPC retention cannot increase its authenticated reservation."); }
                remaining = retainedBytes;
            }
            Release(original - retainedBytes);
        }
        public void Dispose()
        {
            long released;
            lock (sync) { released = remaining; remaining = 0; }
            if (released != 0) { Release(released); }
        }
    }
}
