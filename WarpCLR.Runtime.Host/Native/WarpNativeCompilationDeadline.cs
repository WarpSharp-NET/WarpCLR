using System.Diagnostics;

namespace WarpCLR.Runtime.Host.Native;

internal static class WarpNativeCompilationDeadline
{
    public static async Task<T> RunAsync<T>(TimeSpan timeout, Func<CancellationToken, Task<T>> pipeline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        cancellationToken.ThrowIfCancellationRequested();
        long started = Stopwatch.GetTimestamp();
        using var deadline = new CancellationTokenSource(timeout);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            T result = await pipeline(lifetime.Token).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            lifetime.Token.ThrowIfCancellationRequested();
            // Timer callbacks can be delayed by a busy thread pool; elapsed time is an independent admission check.
            if (Stopwatch.GetElapsedTime(started) >= timeout)
            {
                throw new WarpHostException("WRPNATIVE2004", "The native compilation pipeline exceeded its aggregate deadline.");
            }

            return result;
        }
        catch (OperationCanceledException error)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                var cancelled = new OperationCanceledException(error.Message, error, cancellationToken);
                CopyDiagnostics(error, cancelled);
                throw cancelled;
            }

            if (deadline.IsCancellationRequested || Stopwatch.GetElapsedTime(started) >= timeout)
            {
                var expired = new WarpHostException("WRPNATIVE2004", "The native compilation pipeline exceeded its aggregate deadline. " + error.Message, error);
                CopyDiagnostics(error, expired);
                throw expired;
            }

            throw;
        }
    }

    private static void CopyDiagnostics(Exception source, Exception target)
    {
        foreach (System.Collections.DictionaryEntry entry in source.Data)
        {
            target.Data[entry.Key] = entry.Value;
        }
    }
}
