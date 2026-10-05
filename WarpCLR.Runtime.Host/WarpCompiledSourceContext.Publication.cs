using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCompiledSourceContext
{
    // A returned source tuple can wait for an inherited output root to be released.
    // Its authenticated completed state is republished without re-executing source.
    internal uint CommitRetainedSource(WarpCompiledWorkerTicket ticket)
    {
        lock (executionIdentityGate)
        {
            ValidateTicket(ticket);
            if (stopped != 0 || remoteCensus is not null || helpers[ticket.Worker] is not null ||
                states[ticket.Worker][WarpLogicalMachineLayout.StatusOffset] is not
                    (WarpLogicalMachineLayout.Completed or WarpLogicalMachineLayout.Faulted))
            { throw new InvalidOperationException("Only an exact retained terminal source outcome can be committed without another source quantum."); }
            ticket.BeginExecution();
            ticket.FinishExecution();
        }
        return Commit(ticket);
    }

    private uint CommitSource(WarpCompiledControllerGrant grant, WarpCompiledWorkerTicket ticket)
    {
        uint worker = ticket.Worker;
        uint[] state = states[worker];
        WarpCompiledSourceLocation location = state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Faulted
            ? Plan.FaultLocation(state) : Plan.Location(state);
        if (State == WarpPortableSchedulerLayout.Active)
        {
            MirrorSourceState(grant, ticket, location);
        }
        if (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Faulted)
        {
            return QuarantineSource(grant, ticket, state[WarpLogicalMachineLayout.FaultKindOffset], location);
        }
        if (sourceLeases[worker] && state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable &&
            state[WarpCompiledSourceBoundary.StateOffset] != WarpCompiledSourceBoundary.BeforeSource)
        {
            // An owner-field helper may have published only part of its triple.
            // Keep the prior precise roots and exclusive loan until its entire operation completes.
            return Invoke(grant, nameof(WarpPortableSchedulerServices.YieldWorker), [worker, ticket.Generation]);
        }
        if (State != WarpPortableSchedulerLayout.Active)
        {
            if (sourceLeases[worker])
            {
                AbortSourceLoan(grant, ticket);
            }
            return Invoke(grant, nameof(WarpPortableSchedulerServices.CancelWorker), [worker, ticket.Generation]);
        }
        if (sourceLeases[worker])
        {
            RequireSuccess(Invoke(grant, nameof(WarpPortableSchedulerServices.CaptureServiceResult),
                [worker, ticket.Generation, Arena[WarpPortableHeapLayout.PendingResult] != 0 ? 1u : 0u]));
        }
        uint status = Publish(grant, worker, ticket.Generation, location);
        if (status != 0)
        {
            return QuarantineSource(grant, ticket, 4, location);
        }
        if (sourceLeases[worker])
        {
            FinishSourceLoan(grant, ticket);
        }
        if (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Completed)
        {
            return CompleteSource(grant, ticket);
        }
        if (Header(WarpPortableSchedulerLayout.GCState) == WarpPortableSchedulerLayout.GCRequested)
        {
            return Invoke(grant, nameof(WarpPortableSchedulerServices.ParkForCollection), [worker, ticket.Generation, Epoch]);
        }
        return Invoke(grant, nameof(WarpPortableSchedulerServices.YieldWorker), [worker, ticket.Generation]);
    }

    private uint CommitHelper(WarpCompiledControllerGrant grant, WarpCompiledWorkerTicket ticket,
        WarpCompiledServiceContinuation helper)
    {
        uint worker = ticket.Worker;
        if (helper.Status == WarpLogicalMachineLayout.Runnable)
        {
            return Invoke(grant, nameof(WarpPortableSchedulerServices.YieldWorker), [worker, ticket.Generation]);
        }
        if (State == WarpPortableSchedulerLayout.Active && helper.Result == WarpPortableHeapLayout.Busy &&
            Header(WarpPortableSchedulerLayout.GCState) == WarpPortableSchedulerLayout.GCRequested &&
            Arena[WarpPortableHeapLayout.PendingResult] == 0)
        {
            RequireSuccess(Invoke(grant, nameof(WarpPortableSchedulerServices.CaptureServiceResult), [worker, ticket.Generation, 0]));
            RequireSuccess(Publish(grant, worker, ticket.Generation, new(0, 0, null, null)));
            uint retryRevision = Arena[Worker(worker) + WarpPortableSchedulerLayout.RootRevision];
            RequireSuccess(Invoke(grant, nameof(WarpPortableSchedulerServices.AcknowledgeHeapResult), [worker, ticket.Generation, retryRevision]));
            RequireSuccess(Invoke(grant, nameof(WarpPortableSchedulerServices.ReleaseHeapService), [worker, ticket.Generation]));
            helpers[worker] = null;
            CooperativeHeapRetries = checked(CooperativeHeapRetries + 1);
            return Invoke(grant, nameof(WarpPortableSchedulerServices.ParkForCollection), [worker, ticket.Generation, Epoch]);
        }
        if (State != WarpPortableSchedulerLayout.Active || helper.Result != 0)
        {
            if (State == WarpPortableSchedulerLayout.Active)
            {
                uint fault = Invoke(grant, nameof(WarpPortableSchedulerServices.RecordSourceMachineFault),
                    [worker, ticket.Generation, 4, 0, 0, 0, 0]);
                faults[worker] = new(Plan.Identity, Identity, Dispatch, worker, ticket.Generation, 4,
                    new(0, 0, null, null), []);
                helpers[worker] = null;
                allocations[worker] = 0;
                return fault;
            }
            RequireSuccess(Invoke(grant, nameof(WarpPortableSchedulerServices.AbortHeapService), [worker, ticket.Generation]));
            helpers[worker] = null;
            allocations[worker] = 0;
            return Invoke(grant, nameof(WarpPortableSchedulerServices.CancelWorker), [worker, ticket.Generation]);
        }
        PublishEntryAllocationArguments(grant, ticket);
        var location = new WarpCompiledSourceLocation(0, 0, null, null);
        RequireSuccess(Publish(grant, worker, ticket.Generation, location));
        uint revision = Arena[Worker(worker) + WarpPortableSchedulerLayout.RootRevision];
        RequireSuccess(Invoke(grant, nameof(WarpPortableSchedulerServices.AcknowledgeHeapResult), [worker, ticket.Generation, revision]));
        RequireSuccess(Invoke(grant, nameof(WarpPortableSchedulerServices.ReleaseHeapService), [worker, ticket.Generation]));
        helpers[worker] = null;
        allocations[worker] = 0;
        if (Header(WarpPortableSchedulerLayout.GCState) == WarpPortableSchedulerLayout.GCRequested)
        {
            return Invoke(grant, nameof(WarpPortableSchedulerServices.ParkForCollection), [worker, ticket.Generation, Epoch]);
        }
        return Invoke(grant, nameof(WarpPortableSchedulerServices.YieldWorker), [worker, ticket.Generation]);
    }

    private uint CompleteSource(WarpCompiledControllerGrant grant, WarpCompiledWorkerTicket ticket)
    {
        uint entry = Worker(ticket.Worker);
        uint destination = checked(Arena[entry + WarpPortableSchedulerLayout.LogicalStackBase] + (uint)Plan.RootBankWords);
        uint[] results = Enumerable.Range(0, Plan.Layout.ResultWordCount)
            .Select(word => states[ticket.Worker][Plan.Layout.GetResultWordOffset(word, Plan.MaximumDepth)]).ToArray();
        Plan.Services.Store(Arena, grant, destination, results, Plan.Quantum);
        if (!Plan.ResultRootWords.IsEmpty)
        {
            uint status = Invoke(grant, nameof(WarpPortableSchedulerServices.PublishOutputRoots),
                [ticket.Worker, ticket.Generation, Dispatch]);
            if (status != 0) { return status; }
            RequireSuccess(Invoke(grant, nameof(WarpPortableSchedulerServices.AcknowledgeOutputRoots),
                [ticket.Worker, ticket.Generation, Dispatch]));
        }
        return Invoke(grant, nameof(WarpPortableSchedulerServices.CompleteWorker), [ticket.Worker, ticket.Generation]);
    }

    private uint Publish(WarpCompiledControllerGrant grant, uint worker, uint generation, WarpCompiledSourceLocation location)
    {
        uint[] roots = Plan.ProjectRoots(states[worker], inputs, worker);
        uint entry = Worker(worker);
        Plan.Services.Publish(Arena, grant, Arena[entry + WarpPortableSchedulerLayout.LogicalStackBase], roots, Plan.Quantum);
        return Invoke(grant, nameof(WarpPortableSchedulerServices.PublishRoots),
            [worker, generation, Epoch, Plan.RootMap(location), unchecked(Arena[entry + WarpPortableSchedulerLayout.RootRevision] + 1),
                checked((uint)location.Function), checked((uint)location.ProgramCounter)]);
    }

    private void MirrorSourceState(WarpCompiledControllerGrant grant, WarpCompiledWorkerTicket ticket, WarpCompiledSourceLocation location)
    {
        uint[] state = states[ticket.Worker];
        ulong remaining = state[WarpLogicalMachineLayout.RemainingStepsLowOffset] |
            ((ulong)state[WarpLogicalMachineLayout.RemainingStepsHighOffset] << 32);
        ulong spent = checked((ulong)Plan.MaximumSteps - remaining);
        uint status = WarpCompiledRuntimeServices.Run(mirror, Arena,
            [Scheduler, grant.Token, ticket.Worker, ticket.Generation,
                unchecked((uint)remaining), checked((uint)(remaining >> 32)),
                unchecked((uint)spent), checked((uint)(spent >> 32)), state[WarpLogicalMachineLayout.LogicalDepthOffset],
                checked((uint)location.Function), location.CilOffset, location.Instruction], Plan.Quantum);
        RequireSuccess(status);
    }

    private static void RequireSuccess(uint status)
    {
        if (status != 0)
        {
            throw new InvalidOperationException("A bound generated runtime protocol rejected its exact continuation state: " +
                status.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
