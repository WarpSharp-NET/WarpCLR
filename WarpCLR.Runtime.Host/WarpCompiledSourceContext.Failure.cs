namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCompiledSourceContext
{
    // A local paused proof does not authenticate a stopped remote child. Parent
    // integration must consume the worker registry's recovery capability first.
    internal uint QuarantineStoppedExecutionFailure(WarpCompiledWorkerTicket ticket, WarpCompiledPausedCensus census) =>
        DisposePausedCensus(census, ticket);
}
