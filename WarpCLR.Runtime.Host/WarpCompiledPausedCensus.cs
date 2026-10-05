using System.Collections.Immutable;

namespace WarpCLR.Runtime.Host;

internal sealed class WarpCompiledPausedCensus
{
    internal WarpCompiledPausedCensus(WarpCompiledSourceContext context, uint dispatch, uint epoch,
        WarpCompiledControllerGrant? grant, ImmutableArray<WarpCompiledPausedWorker> workers)
    {
        Context = context;
        Dispatch = dispatch;
        Epoch = epoch;
        Grant = grant;
        Workers = workers;
    }

    internal WarpCompiledSourceContext Context { get; }
    internal uint Dispatch { get; }
    internal uint Epoch { get; }
    internal WarpCompiledControllerGrant? Grant { get; }
    internal ImmutableArray<WarpCompiledPausedWorker> Workers { get; }
    private int used;
    internal void Consume()
    {
        if (Interlocked.CompareExchange(ref used, 1, 0) != 0)
        {
            throw new InvalidOperationException("An authenticated paused census is single-use.");
        }
    }
}
