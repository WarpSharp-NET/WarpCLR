using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCompiledSourceContext
{
    internal WarpCompiledPausedCensus PrepareRemoteSource(WarpCompiledWorkerTicket ticket, WarpCompiledControllerGrant? heldGrant)
    {
        WarpCompiledControllerGrant grant = heldGrant ?? controller.TryAcquire()
            ?? throw new InvalidOperationException("Remote admission yields until its controller can be acquired.");
        bool suspended = false;
        try
        {
            lock (executionIdentityGate)
            {
                ValidateTicket(ticket);
                if (stopped != 0 || remoteCensus is not null || helpers[ticket.Worker] is not null || ticket.Executed ||
                    states[ticket.Worker][WarpLogicalMachineLayout.StatusOffset] != WarpLogicalMachineLayout.Runnable)
                { throw new InvalidOperationException("Remote source admission requires one exact unused source continuation."); }
                WarpCompiledPausedCensus census = SnapshotPausedCensus(heldGrant);
                controller.SuspendForRemote(grant, heldGrant is not null);
                suspended = true;
                remoteCensus = census;
                return census;
            }
        }
        finally { if (!suspended && heldGrant is null) { controller.Release(grant); } }
    }

    internal uint[] RemoteSourceState(WarpCompiledWorkerTicket ticket) => states[ticket.Worker];
    internal uint[][] RemoteSourceInputs => inputs;

    internal void BeginRemoteSource(WarpCompiledPausedCensus census, WarpCompiledWorkerTicket ticket)
    {
        lock (executionIdentityGate)
        {
            RequireRemoteCensus(census);
            ValidateTicket(ticket);
            uint[] state = states[ticket.Worker];
            bool ordinary = State == WarpPortableSchedulerLayout.Active && Header(WarpPortableSchedulerLayout.GCState) == WarpPortableSchedulerLayout.GCIdle;
            bool owned = sourceLeases[ticket.Worker] && sourceLoanStarted[ticket.Worker] &&
                state[WarpCompiledSourceBoundary.StateOffset] != WarpCompiledSourceBoundary.BeforeSource;
            if (state[WarpLogicalMachineLayout.StatusOffset] != WarpLogicalMachineLayout.Runnable || !(ordinary || owned))
            { throw new InvalidOperationException("A parked or terminal source continuation cannot start an owned remote quantum."); }
            sourceCheckpoints.Remove(state);
            ticket.BeginExecution();
            if (state[WarpCompiledSourceBoundary.StateOffset] == WarpCompiledSourceBoundary.BeforeSource)
            {
                WarpCompiledSourceBoundary.Acknowledge(state);
                sourceLoanStarted[ticket.Worker] = sourceLeases[ticket.Worker];
            }
        }
    }

    internal void FinishRemoteSource(WarpCompiledPausedCensus census, WarpCompiledWorkerTicket ticket, WarpCoreCLRWorkerLease source)
    {
        lock (executionIdentityGate)
        {
            RequireRemoteCensus(census);
            ValidateTicket(ticket);
            WarpCoreCLRWorkerProcess.WordCheckpoint words = source.RequireCommittedWordCheckpoint(states[ticket.Worker], Arena);
            var checkpoint = new SourceCheckpoint(this, ticket, source, words, sourceCheckpointAuthority);
            executionQuanta[ticket.Worker] = checked(executionQuanta[ticket.Worker] + 1);
            ticket.FinishExecution();
            controller.ResumeAfterRemoteCommit(census.Grant);
            remoteCensus = null;
            sourceCheckpoints.Remove(states[ticket.Worker]);
            sourceCheckpoints.Add(states[ticket.Worker], checkpoint);
        }
    }

    internal void StopRemoteSource(WarpCompiledPausedCensus census, WarpCompiledWorkerTicket ticket,
        WarpCoreCLRQuarantineRecovery recovery, IReadOnlyList<WarpCoreCLRCommandAdmission> admissions)
    {
        lock (executionIdentityGate)
        {
            RequireRemoteCensus(census);
            ValidateTicket(ticket);
            if (!ReferenceEquals(recovery.Admission.ExactTicket, ticket) || !ReferenceEquals(recovery.Admission.State, states[ticket.Worker]) ||
                !ReferenceEquals(recovery.Admission.Arena, Arena) || recovery.Census.Count != Plan.Workers || admissions.Count != Plan.Workers ||
                admissions.Any(item => !recovery.Census.Admissions.Any(actual => ReferenceEquals(actual, item))))
            { throw new InvalidOperationException("The authenticated stopped registry differs from the exact logical context census."); }
            foreach (WarpCompiledPausedWorker worker in census.Workers)
            {
                if (!ReferenceEquals(tickets[worker.Worker], worker.Ticket) || !ReferenceEquals(states[worker.Worker], worker.SourceState) ||
                    !ReferenceEquals(helpers[worker.Worker], worker.Helper) || executionQuanta[worker.Worker] != worker.Quanta ||
                    worker.Worker != ticket.Worker && tickets[worker.Worker]?.Executing == true ||
                    WorkerState(worker.Worker) != worker.State || Arena[Worker(worker.Worker) + WarpPortableSchedulerLayout.RunGeneration] != worker.RunGeneration)
                { throw new InvalidOperationException("An exact paused continuation changed before stopped-child cleanup."); }
            }
            census.Consume();
            Volatile.Write(ref stopped, 1);
            ticket.FinishExecution();
            WarpCompiledSourceLocation location = Plan.Location(states[ticket.Worker]);
            faults[ticket.Worker] = new(Plan.Identity, Identity, Dispatch, ticket.Worker, ticket.Generation, 5,
                location, Plan.SourceFrames(states[ticket.Worker]));
        }
    }

    internal void FinishRemoteDisposal(WarpCompiledPausedCensus census)
    {
        lock (executionIdentityGate)
        {
            RequireRemoteCensus(census);
            if (stopped == 0 || State != WarpPortableSchedulerLayout.DisposedContext || Header(WarpPortableSchedulerLayout.OutputQuarantined) != 1 ||
                Header(WarpPortableSchedulerLayout.ControllerOwner) != 0)
            { throw new InvalidOperationException("Generated stopped-census cleanup did not confirm final disposal and quarantine."); }
            if (census.Grant is not null) { controller.AcknowledgeRemoteRelease(census.Grant); }
            ClearDisposedContinuations();
            remoteCensus = null;
        }
    }

    internal void SuppressUnresolvedRemoteFailure()
    {
        lock (executionIdentityGate) { Volatile.Write(ref stopped, 1); }
    }

    private void RequireRemoteCensus(WarpCompiledPausedCensus census)
    {
        if (!ReferenceEquals(remoteCensus, census) || !ReferenceEquals(census.Context, this) || census.Dispatch != Dispatch || census.Epoch != Epoch)
        { throw new InvalidOperationException("The remote ownership census belongs to another context or generation."); }
    }
}
