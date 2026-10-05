using System.Collections.Immutable;

namespace WarpCLR.Runtime.Host;

internal sealed class WarpCompiledRemotePreparedRun(WarpCompiledPausedCensus census, WarpCompiledWorkerTicket ticket,
    WarpCoreCLRCommandAdmission admission, ImmutableArray<WarpCoreCLRCommandAdmission> admissions,
    ImmutableArray<WarpCoreCLRPreparedCleanup> cleanup)
{
    internal WarpCompiledPausedCensus Census { get; } = census;
    internal WarpCompiledWorkerTicket Ticket { get; } = ticket;
    internal WarpCoreCLRCommandAdmission Admission { get; } = admission;
    internal ImmutableArray<WarpCoreCLRCommandAdmission> Admissions { get; } = admissions;
    internal ImmutableArray<WarpCoreCLRPreparedCleanup> Cleanup { get; } = cleanup;
    private int used;
    internal void Begin()
    {
        if (Interlocked.CompareExchange(ref used, 1, 0) != 0)
        { throw new InvalidOperationException("An exact admitted remote source command is single-use."); }
    }
}
