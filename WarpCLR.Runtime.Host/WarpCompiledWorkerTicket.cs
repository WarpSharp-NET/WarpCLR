namespace WarpCLR.Runtime.Host;

internal sealed class WarpCompiledWorkerTicket
{
    internal WarpCompiledWorkerTicket(WarpCompiledSourceContext context, uint worker, uint physical, uint generation, uint dispatch)
    {
        Context = context;
        Worker = worker;
        Physical = physical;
        Generation = generation;
        Dispatch = dispatch;
    }

    internal WarpCompiledSourceContext Context { get; }
    internal uint Worker { get; }
    internal uint Physical { get; }
    internal uint Generation { get; }
    internal uint Dispatch { get; }
    private int executionState;
    internal bool Executed => Volatile.Read(ref executionState) == 2;
    internal bool Executing => Volatile.Read(ref executionState) == 1;
    internal void BeginExecution()
    {
        if (Interlocked.CompareExchange(ref executionState, 1, 0) != 0)
        {
            throw new InvalidOperationException("An owned physical continuation cannot be resumed concurrently or twice.");
        }
    }
    internal void FinishExecution() => Volatile.Write(ref executionState, 2);
    internal bool Released { get; set; }
}
