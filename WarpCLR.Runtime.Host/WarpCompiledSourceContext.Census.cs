using System.Collections.Immutable;
using WarpCLR.Compiler;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCompiledSourceContext
{
    // This local adapter proves synchronous generated continuations are paused.
    // The remote adapter must substitute its authenticated stopped-command census;
    // this local proof never establishes that an external process has terminated.
    internal WarpCompiledPausedCensus? CapturePausedCensus(WarpCompiledControllerGrant? heldGrant = null)
    {
        WarpCompiledControllerGrant? grant = heldGrant ?? controller.TryAcquire();
        if (grant is null) { return null; }
        controller.Validate(grant);
        try
        {
            lock (executionIdentityGate) { return SnapshotPausedCensus(heldGrant); }
        }
        finally { if (heldGrant is null) { controller.Release(grant); } }
    }

    private WarpCompiledPausedCensus SnapshotPausedCensus(WarpCompiledControllerGrant? heldGrant)
    {
            var workers = ImmutableArray.CreateBuilder<WarpCompiledPausedWorker>(checked((int)Plan.Workers));
            for (uint worker = 0; worker < Plan.Workers; worker++)
            {
                WarpCompiledWorkerTicket? ticket = tickets[worker];
                if (ticket?.Executing == true)
                {
                    throw new InvalidOperationException("An active generated continuation must stop before a paused census can be admitted.");
                }
                uint entry = Worker(worker);
                if ((WorkerState(worker) == WarpPortableSchedulerLayout.Running) != (ticket is not null))
                {
                    throw new InvalidOperationException("The admitted logical census differs from the owned physical continuations.");
                }
                workers.Add(new(worker, WorkerState(worker), Arena[entry + WarpPortableSchedulerLayout.RunGeneration],
                    Arena[entry + WarpPortableSchedulerLayout.PhysicalOwner], executionQuanta[worker], ticket,
                    ticket?.Executed == true, states[worker], helpers[worker]));
            }
            return new(this, Dispatch, Epoch, heldGrant, workers.MoveToImmutable());
    }

    internal uint DisposePausedCensus(WarpCompiledPausedCensus census, WarpCompiledWorkerTicket? failedTicket = null)
    {
        if (!ReferenceEquals(census.Context, this)) { throw new InvalidOperationException("A paused census belongs to another context."); }
        WarpCompiledControllerGrant? grant = census.Grant ?? controller.TryAcquire();
        if (grant is null) { return WarpPortableSchedulerLayout.Yield; }
        try
        {
            lock (executionIdentityGate)
            {
                ValidatePausedCensus(census, grant);
                if (failedTicket is not null) { ValidateTicket(failedTicket); }
                census.Consume();
                Volatile.Write(ref stopped, 1);
            }
            WarpCompiledSourceLocation location = failedTicket is null ? new(0, 0, null, null) : Plan.Location(states[failedTicket.Worker]);
            uint worker = failedTicket?.Worker ?? 0;
            faults[worker] = new(Plan.Identity, Identity, Dispatch, worker, failedTicket?.Generation ?? 0, 5,
                location, failedTicket is null ? [] : Plan.SourceFrames(states[worker]));
            uint status = Invoke(grant, nameof(WarpPortableSchedulerServices.DisposeStoppedCensus),
                [census.Dispatch, census.Epoch, failedTicket?.Worker ?? WarpPortableSchedulerLayout.NoWorker,
                    failedTicket?.Generation ?? 0, checked((uint)location.Function), location.CilOffset,
                    location.Instruction, failedTicket is null ? 0 : states[worker][WarpCLR.IR.WarpLogicalMachineLayout.LogicalDepthOffset]]);
            if (status != 0) { return status; }
            ClearDisposedContinuations();
            return 0;
        }
        finally { controller.Release(grant); }
    }

    private void ValidatePausedCensus(WarpCompiledPausedCensus census, WarpCompiledControllerGrant grant)
    {
        controller.Validate(grant);
        if (census.Dispatch != Dispatch || census.Epoch != Epoch || census.Workers.Length != Plan.Workers)
        {
            throw new InvalidOperationException("The paused context generation/census changed before cleanup.");
        }
        foreach (WarpCompiledPausedWorker item in census.Workers)
        {
            uint worker = item.Worker;
            uint entry = Worker(worker);
            if (item.State != WorkerState(worker) || item.RunGeneration != Arena[entry + WarpPortableSchedulerLayout.RunGeneration] ||
                item.Physical != Arena[entry + WarpPortableSchedulerLayout.PhysicalOwner] || item.Quanta != executionQuanta[worker] ||
                !ReferenceEquals(item.Ticket, tickets[worker]) || item.Executed != (tickets[worker]?.Executed == true) ||
                tickets[worker]?.Executing == true || !ReferenceEquals(item.SourceState, states[worker]) || !ReferenceEquals(item.Helper, helpers[worker]))
            {
                throw new InvalidOperationException("A worker continuation differs from its exact admitted paused census.");
            }
        }
    }

    private void ClearDisposedContinuations()
    {
        for (uint worker = 0; worker < Plan.Workers; worker++)
        {
            if (tickets[worker] is { } ticket) { ReleaseTicket(ticket); }
            helpers[worker] = null;
            allocations[worker] = 0;
            sourceLeases[worker] = false;
            sourceLoanStarted[worker] = false;
        }
    }
}
