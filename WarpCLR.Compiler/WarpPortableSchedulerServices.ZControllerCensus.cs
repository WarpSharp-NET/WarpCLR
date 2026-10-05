namespace WarpCLR.Compiler;

internal static partial class WarpPortableSchedulerServices
{
    // Only an authenticated, stopped remote controller census may invoke this
    // service. The operation and both generations are fixed before execution;
    // they are not a caller assertion that source continuations have stopped.
    public static uint DisposeStoppedController(uint[] arena, uint scheduler, uint controller, uint operation,
        uint initialDispatch, uint initialEpoch, uint dispatch, uint epoch)
    {
        uint status = Begin(arena, scheduler, controller, 14);
        if (status != 0) { return status; }
        if (ValidControllerGenerations(arena, scheduler, operation, initialDispatch, initialEpoch, dispatch, epoch) == 0 ||
            ValidControllerRows(arena, scheduler) == 0)
        { return WarpPortableSchedulerLayout.Invalid; }
        NormalizeStoppedControllerRows(arena, scheduler);
        if (arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] != 0)
        {
            arena[WarpPortableHeapLayout.CollectionEpoch] = epoch;
        }
        return DisposeStoppedCensus(arena, scheduler, controller, dispatch, epoch,
            WarpPortableSchedulerLayout.NoWorker, 0, 0, 0, 0, 0);
    }

    private static uint ValidControllerGenerations(uint[] arena, uint scheduler, uint operation,
        uint initialDispatch, uint initialEpoch, uint dispatch, uint epoch)
    {
        if (dispatch != arena[scheduler + WarpPortableSchedulerLayout.DispatchGeneration] ||
            epoch != arena[scheduler + WarpPortableSchedulerLayout.GCEpoch]) { return 0; }
        if (operation == 1)
        {
            if (dispatch != initialDispatch || (epoch != initialEpoch &&
                (initialEpoch == 0xFFFFFFFFu || epoch != initialEpoch + 1))) { return 0; }
        }
        else if (operation == 2)
        {
            if (epoch != initialEpoch || (dispatch != initialDispatch &&
                (initialDispatch == 0xFFFFFFFFu || dispatch != initialDispatch + 1))) { return 0; }
        }
        else { return 0; }
        if (arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] != 0)
        {
            uint heapEpoch = arena[WarpPortableHeapLayout.CollectionEpoch];
            if (heapEpoch != initialEpoch && (operation != 1 || initialEpoch == 0xFFFFFFFFu || heapEpoch != initialEpoch + 1))
            { return 0; }
        }
        return 1;
    }

    private static uint ValidControllerRows(uint[] arena, uint scheduler)
    {
        uint running = 0;
        uint count = arena[scheduler + WarpPortableSchedulerLayout.WorkerCount];
        uint residents = arena[scheduler + WarpPortableSchedulerLayout.ResidentCount];
        uint lease = LeaseOwner(arena, scheduler);
        if (lease != WarpPortableSchedulerLayout.NoWorker && lease >= count) { return 0; }
        for (uint worker = 0; worker < count; worker++)
        {
            uint entry = Worker(arena, scheduler, worker);
            uint state = arena[entry + WarpPortableSchedulerLayout.WorkerState];
            if (state < WarpPortableSchedulerLayout.Ready || state > WarpPortableSchedulerLayout.WaitingOutput) { return 0; }
            if (state != WarpPortableSchedulerLayout.Running) { continue; }
            uint resident = arena[entry + WarpPortableSchedulerLayout.PhysicalOwner];
            if (resident >= residents || arena[entry + WarpPortableSchedulerLayout.RunGeneration] == 0) { return 0; }
            uint physical = Physical(arena, scheduler, resident);
            if (arena[physical + WarpPortableSchedulerLayout.PhysicalState] != 1 ||
                arena[physical + WarpPortableSchedulerLayout.PhysicalWorker] != worker ||
                arena[physical + WarpPortableSchedulerLayout.PhysicalGeneration] != arena[entry + WarpPortableSchedulerLayout.RunGeneration]) { return 0; }
            running++;
        }
        return ValidStoppedPhysical(arena, scheduler, running);
    }

    private static uint NormalizeStoppedControllerRows(uint[] arena, uint scheduler)
    {
        uint running = 0;
        uint terminal = 0;
        uint parked = 0;
        uint heapActive = 0;
        for (uint worker = 0; worker < arena[scheduler + WarpPortableSchedulerLayout.WorkerCount]; worker++)
        {
            uint state = arena[Worker(arena, scheduler, worker) + WarpPortableSchedulerLayout.WorkerState];
            uint done = Terminal(state);
            terminal += done;
            running += state == WarpPortableSchedulerLayout.Running ? 1u : 0u;
            parked += state == WarpPortableSchedulerLayout.ParkedGC ? 1u : 0u;
            if (arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] != 0)
            {
                uint heapState = done != 0 ? 0u : state == WarpPortableSchedulerLayout.ParkedGC ? 2u : 1u;
                arena[arena[WarpPortableHeapLayout.WorkerStart] + worker * WarpPortableHeapLayout.WorkerWords] = heapState;
                heapActive += heapState != 0 ? 1u : 0u;
            }
        }
        arena[scheduler + WarpPortableSchedulerLayout.RunningCount] = running;
        arena[scheduler + WarpPortableSchedulerLayout.TerminalCount] = terminal;
        arena[scheduler + WarpPortableSchedulerLayout.GCParkedCount] = parked;
        if (arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] != 0)
        {
            arena[WarpPortableHeapLayout.ActiveWorkers] = heapActive;
            arena[WarpPortableHeapLayout.ParkedWorkers] = parked;
        }
        return 0;
    }
}
