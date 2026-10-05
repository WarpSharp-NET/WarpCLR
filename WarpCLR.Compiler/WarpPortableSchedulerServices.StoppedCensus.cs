namespace WarpCLR.Compiler;

internal static partial class WarpPortableSchedulerServices
{
    // Trusted runtime admission must authenticate every worker continuation and
    // stopped arena transaction before invoking this compiled census operation.
    // There is deliberately no caller-supplied "all stopped" scalar flag.
    public static uint DisposeStoppedCensus(uint[] arena, uint scheduler, uint controller, uint dispatch, uint epoch,
        uint failedWorker, uint failedGeneration, uint function, uint cilOffset, uint instruction, uint depth)
    {
        // Operation14 admits the frozen collector state so an authenticated
        // stopped collection can dispose, while ordinary services still yield.
        uint status = Begin(arena, scheduler, controller, 14);
        if (status != 0) { return status; }
        if (dispatch != arena[scheduler + WarpPortableSchedulerLayout.DispatchGeneration] ||
            epoch != arena[scheduler + WarpPortableSchedulerLayout.GCEpoch] || ValidStoppedCensus(arena, scheduler) == 0 ||
            (failedWorker != WarpPortableSchedulerLayout.NoWorker &&
                (RequireRunning(arena, scheduler, failedWorker, failedGeneration) != 0 ||
                    depth > arena[Worker(arena, scheduler, failedWorker) + WarpPortableSchedulerLayout.UserStackLimit])) ||
            (failedWorker == WarpPortableSchedulerLayout.NoWorker && (failedGeneration != 0 || function != 0 || cilOffset != 0 || instruction != 0 || depth != 0)))
        {
            return WarpPortableSchedulerLayout.Invalid;
        }
        arena[scheduler + WarpPortableSchedulerLayout.Operation] = 31;
        uint winner = failedWorker == WarpPortableSchedulerLayout.NoWorker ? 0u : failedWorker;
        StoreFault(arena, scheduler, winner, WarpPortableSchedulerLayout.RuntimeInvariantFault, 0, function,
            cilOffset, instruction, depth, 0, 0, 0);
        arena[scheduler + WarpPortableSchedulerLayout.ContextState] = WarpPortableSchedulerLayout.Quarantined;
        arena[scheduler + WarpPortableSchedulerLayout.OutputQuarantined] = 1;
        uint owner = LeaseOwner(arena, scheduler);
        if (owner != WarpPortableSchedulerLayout.NoWorker) { AbandonLease(arena, scheduler, owner); }
        QuarantineFault(arena, scheduler, winner);
        for (uint worker = 0; worker < arena[scheduler + WarpPortableSchedulerLayout.WorkerCount]; worker++)
        {
            if (Terminal(arena[Worker(arena, scheduler, worker) + WarpPortableSchedulerLayout.WorkerState]) == 0)
            {
                CompleteTerminal(arena, scheduler, worker, WarpPortableSchedulerLayout.Cancelled);
            }
        }
        arena[scheduler + WarpPortableSchedulerLayout.GCState] = WarpPortableSchedulerLayout.GCIdle;
        if (arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] != 0)
        {
            arena[WarpPortableHeapLayout.CollectionState] = WarpPortableHeapLayout.Idle;
        }
        status = RequestDisposal(arena, scheduler, controller);
        return status != 0 ? status : FinishDisposal(arena, scheduler, controller);
    }

    private static uint ValidStoppedCensus(uint[] arena, uint scheduler)
    {
        uint running = 0;
        uint terminal = 0;
        uint parked = 0;
        uint count = arena[scheduler + WarpPortableSchedulerLayout.WorkerCount];
        uint residents = arena[scheduler + WarpPortableSchedulerLayout.ResidentCount];
        uint owner = LeaseOwner(arena, scheduler);
        if (owner != WarpPortableSchedulerLayout.NoWorker && owner >= count) { return 0; }
        for (uint worker = 0; worker < count; worker++)
        {
            uint entry = Worker(arena, scheduler, worker);
            uint state = arena[entry + WarpPortableSchedulerLayout.WorkerState];
            if (state < WarpPortableSchedulerLayout.Ready || state > WarpPortableSchedulerLayout.WaitingOutput) { return 0; }
            terminal += Terminal(state);
            parked += state == WarpPortableSchedulerLayout.ParkedGC ? 1u : 0u;
            if (state == WarpPortableSchedulerLayout.Running)
            {
                uint resident = arena[entry + WarpPortableSchedulerLayout.PhysicalOwner];
                if (resident >= residents || arena[entry + WarpPortableSchedulerLayout.RunGeneration] == 0) { return 0; }
                uint physical = Physical(arena, scheduler, resident);
                if (arena[physical + WarpPortableSchedulerLayout.PhysicalState] != 1 ||
                    arena[physical + WarpPortableSchedulerLayout.PhysicalWorker] != worker ||
                    arena[physical + WarpPortableSchedulerLayout.PhysicalGeneration] != arena[entry + WarpPortableSchedulerLayout.RunGeneration]) { return 0; }
                running++;
            }
        }
        return running == arena[scheduler + WarpPortableSchedulerLayout.RunningCount] &&
            terminal == arena[scheduler + WarpPortableSchedulerLayout.TerminalCount] &&
            parked == arena[scheduler + WarpPortableSchedulerLayout.GCParkedCount] &&
            ValidStoppedPhysical(arena, scheduler, running) != 0 ? 1u : 0u;
    }

    private static uint ValidStoppedPhysical(uint[] arena, uint scheduler, uint expected)
    {
        uint occupied = 0;
        for (uint resident = 0; resident < arena[scheduler + WarpPortableSchedulerLayout.ResidentCount]; resident++)
        {
            uint physical = Physical(arena, scheduler, resident);
            uint state = arena[physical + WarpPortableSchedulerLayout.PhysicalState];
            if (state == 0) { continue; }
            if (state != 1 || arena[physical + WarpPortableSchedulerLayout.PhysicalWorker] >= arena[scheduler + WarpPortableSchedulerLayout.WorkerCount]) { return 0; }
            occupied++;
        }
        return occupied == expected ? 1u : 0u;
    }
}
