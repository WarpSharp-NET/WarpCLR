using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCompiledSourceContext
{
    private uint QuarantineSource(WarpCompiledControllerGrant grant, WarpCompiledWorkerTicket ticket,
        uint kind, WarpCompiledSourceLocation location)
    {
        uint worker = ticket.Worker;
        uint[] state = states[worker];
        faults[worker] = new(Plan.Identity, Identity, Dispatch, worker, ticket.Generation,
            kind, location, Plan.SourceFrames(state));
        sourceLeases[worker] = false;
        sourceLoanStarted[worker] = false;
        return Invoke(grant, nameof(WarpPortableSchedulerServices.RecordSourceMachineFault),
            [worker, ticket.Generation, kind, checked((uint)location.Function),
                location.CilOffset, location.Instruction, state[WarpLogicalMachineLayout.LogicalDepthOffset]]);
    }

    private void AbortSourceLoan(WarpCompiledControllerGrant grant, WarpCompiledWorkerTicket ticket)
    {
        RequireSuccess(Invoke(grant, nameof(WarpPortableSchedulerServices.AbortHeapService), [ticket.Worker, ticket.Generation]));
        sourceLeases[ticket.Worker] = false;
        sourceLoanStarted[ticket.Worker] = false;
    }

    private void FinishSourceLoan(WarpCompiledControllerGrant grant, WarpCompiledWorkerTicket ticket)
    {
        uint revision = Arena[Worker(ticket.Worker) + WarpPortableSchedulerLayout.RootRevision];
        RequireSuccess(Invoke(grant, nameof(WarpPortableSchedulerServices.AcknowledgeHeapResult), [ticket.Worker, ticket.Generation, revision]));
        RequireSuccess(Invoke(grant, nameof(WarpPortableSchedulerServices.ReleaseHeapService), [ticket.Worker, ticket.Generation]));
        sourceLeases[ticket.Worker] = false;
        sourceLoanStarted[ticket.Worker] = false;
    }
}
