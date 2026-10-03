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
            cancellationToken.ThrowIfCancellationRequested();
            if (deadline.IsCancellationRequested || Stopwatch.GetElapsedTime(started) >= timeout)
            {
                throw new WarpHostException("WRPNATIVE2004", "The native compilation pipeline exceeded its aggregate deadline.", error);
            }

            throw;
        }
    }
}
