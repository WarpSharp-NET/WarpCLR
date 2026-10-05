using System.Diagnostics;

namespace WarpCLR.Runtime.Host;

internal sealed class WarpCoreCLRWorkerCleanupAttempt : IDisposable
{
    internal const string SemanticId = "warp.coreclr-worker-cleanup/monotonic-single-attempt-prepared-private-threads-byte-pipe/0.2";
    private readonly TimeSpan quota;
    private readonly Task operation;
    private readonly Action<string> record;
    private readonly ManualResetEventSlim requested = new();
    private readonly ManualResetEventSlim finished = new();
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long request;
    private int endedThreads;
    private int preparedThreads;
    private int resourcesReleased;
    private int started;
    private bool provisioningFailed;

    internal WarpCoreCLRWorkerCleanupAttempt(TimeSpan quota, Action<WarpCoreCLRWorkerCleanupAttempt> work, Action<string> record)
    {
        this.quota = quota; this.record = record;
        operation = new(() => { record("core-start"); work(this); });
    }

    internal Task Completion => completion.Task;
    internal TimeSpan Remaining => quota - Stopwatch.GetElapsedTime(request);

    internal void Prepare()
    {
        try
        {
            new Thread(Run) { IsBackground = true, Name = "WarpCLR worker cleanup" }.Start();
            preparedThreads++;
            new Thread(WatchDeadline) { IsBackground = true, Name = "WarpCLR worker cleanup deadline" }.Start();
            preparedThreads++;
        }
        catch
        {
            provisioningFailed = true;
            if (preparedThreads == 0) { Dispose(); }
            else { requested.Set(); }
            throw;
        }
    }

    internal void Start(long timestamp)
    {
        if (Interlocked.CompareExchange(ref started, 1, 0) != 0) { return; }
        request = timestamp;
        record("request");
        requested.Set();
    }

    private void Run()
    {
        try
        {
            requested.Wait();
            if (provisioningFailed) { return; }
            // This preallocated task captures cleanup exceptions on the prepared private thread, without scheduling pool work.
            operation.RunSynchronously(TaskScheduler.Default);
            if (operation.Exception is { } error)
            {
                completion.TrySetException(new WarpHostException("WRPCORECLR3003", "CoreCLR worker cleanup failed; the worker and buffers are quarantined.", error));
                record("failed");
            }
            else if (Remaining <= TimeSpan.Zero) { ExhaustQuota(); }
            else if (completion.TrySetResult()) { record("succeeded"); }
            else { record("late-contained-terminal-failure"); }
        }
        catch (Exception error) when (error is TaskSchedulerException or ThreadStateException or OutOfMemoryException)
        { completion.TrySetException(new WarpHostException("WRPCORECLR3003", "The prepared cleanup execution resource failed; the worker and buffers are quarantined.", error)); }
        finally { finished.Set(); EndThread(); }
    }

    private void WatchDeadline()
    {
        try
        {
            requested.Wait();
            if (provisioningFailed) { return; }
            record("deadline-watch-start");
            TimeSpan remaining;
            while ((remaining = Remaining) > TimeSpan.Zero)
            {
                TimeSpan interval = TimeSpan.FromMilliseconds(Math.Min(remaining.TotalMilliseconds, int.MaxValue));
                if (finished.Wait(interval)) { return; }
            }
            ExhaustQuota();
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or OutOfMemoryException)
        { completion.TrySetException(new WarpHostException("WRPCORECLR3003", "The shared cleanup deadline resource failed; the worker and buffers are quarantined.", error)); }
        finally { EndThread(); }
    }

    private void ExhaustQuota()
    {
        if (completion.TrySetException(new WarpHostException("WRPCORECLR3003", "CoreCLR worker cleanup exhausted its separate bounded quota; the worker and buffers are quarantined.")))
        { record("quota-exhausted"); }
        else { record("late-contained-terminal-failure"); }
    }

    public void Dispose()
    {
        // Active containment keeps these signals alive; the final owned thread releases them exactly once.
        if (Volatile.Read(ref endedThreads) == Volatile.Read(ref preparedThreads) && Interlocked.Exchange(ref resourcesReleased, 1) == 0)
        { requested.Dispose(); finished.Dispose(); }
    }

    private void EndThread()
    {
        if (Interlocked.Increment(ref endedThreads) == preparedThreads) { Dispose(); }
    }
}
