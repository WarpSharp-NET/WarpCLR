namespace WarpCLR.Compiler;

internal static partial class WarpPortableSchedulerServices
{
    public static uint RequestCollection(uint[] arena, uint scheduler, uint controller)
    {
        uint status = Begin(arena, scheduler, controller, 11);
        if (status != 0)
        {
            return status;
        }
        if (arena[scheduler + WarpPortableSchedulerLayout.ContextState] != WarpPortableSchedulerLayout.Active &&
            arena[scheduler + WarpPortableSchedulerLayout.ContextState] != WarpPortableSchedulerLayout.CompletedContext)
        {
            return ProgressStatus(arena, scheduler);
        }
        if (arena[scheduler + WarpPortableSchedulerLayout.GCState] == WarpPortableSchedulerLayout.GCRequested)
        {
            arena[scheduler + WarpPortableSchedulerLayout.Result] = arena[scheduler + WarpPortableSchedulerLayout.GCEpoch];
            return 0;
        }
        uint epoch = arena[scheduler + WarpPortableSchedulerLayout.GCEpoch];
        if (epoch == 0xFFFFFFFFu)
        {
            return RuntimeFault(arena, scheduler, 0, WarpPortableSchedulerLayout.GenerationExhausted);
        }
        if (arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] != 0)
        {
            if (arena[WarpPortableHeapLayout.CollectionState] != WarpPortableHeapLayout.Idle || arena[WarpPortableHeapLayout.CollectionEpoch] != epoch)
            {
                return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, epoch, arena[WarpPortableHeapLayout.CollectionEpoch]);
            }
            arena[WarpPortableHeapLayout.CollectionEpoch] = epoch + 1;
            arena[WarpPortableHeapLayout.CollectionState] = WarpPortableHeapLayout.Requested;
        }
        arena[scheduler + WarpPortableSchedulerLayout.GCState] = WarpPortableSchedulerLayout.GCRequested;
        arena[scheduler + WarpPortableSchedulerLayout.GCEpoch] = epoch + 1;
        arena[scheduler + WarpPortableSchedulerLayout.Result] = epoch + 1;
        return 0;
    }

    public static uint ParkForCollection(uint[] arena, uint scheduler, uint controller, uint worker, uint generation, uint epoch)
    {
        uint status = Begin(arena, scheduler, controller, 12);
        if (status != 0)
        {
            return status;
        }
        if (RequireWorker(arena, scheduler, worker) != 0 || epoch != arena[scheduler + WarpPortableSchedulerLayout.GCEpoch] ||
            arena[scheduler + WarpPortableSchedulerLayout.GCState] != WarpPortableSchedulerLayout.GCRequested)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, epoch);
        }
        uint entry = Worker(arena, scheduler, worker);
        uint prior = arena[entry + WarpPortableSchedulerLayout.WorkerState];
        if (Terminal(prior) != 0 || prior == WarpPortableSchedulerLayout.ParkedGC ||
            arena[entry + WarpPortableSchedulerLayout.RunGeneration] != generation)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, generation);
        }
        if (LeaseOwner(arena, scheduler) == worker)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Yield, worker, LeasePending(arena, scheduler));
        }
        if (arena[entry + WarpPortableSchedulerLayout.SafePoint] == 0 || arena[entry + WarpPortableSchedulerLayout.RootRevision] == 0 ||
            arena[entry + WarpPortableSchedulerLayout.RootEpoch] != epoch)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.NeedRootPublication, worker, epoch);
        }
        ReleasePhysical(arena, scheduler, worker);
        arena[entry + WarpPortableSchedulerLayout.ResumeState] = prior == WarpPortableSchedulerLayout.Running ? WarpPortableSchedulerLayout.Ready : prior;
        arena[entry + WarpPortableSchedulerLayout.WorkerState] = WarpPortableSchedulerLayout.ParkedGC;
        arena[scheduler + WarpPortableSchedulerLayout.GCParkedCount]++;
        if (arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] != 0)
        {
            uint heapWorker = arena[WarpPortableHeapLayout.WorkerStart] + worker * WarpPortableHeapLayout.WorkerWords;
            arena[heapWorker] = 2;
            arena[heapWorker + 1] = epoch;
            arena[WarpPortableHeapLayout.ParkedWorkers]++;
        }
        return 0;
    }

    public static uint BeginCollection(uint[] arena, uint scheduler, uint controller, uint epoch)
    {
        uint status = Begin(arena, scheduler, controller, 13);
        if (status != 0)
        {
            return status;
        }
        uint active = arena[scheduler + WarpPortableSchedulerLayout.WorkerCount] - arena[scheduler + WarpPortableSchedulerLayout.TerminalCount];
        if (epoch != arena[scheduler + WarpPortableSchedulerLayout.GCEpoch] ||
            arena[scheduler + WarpPortableSchedulerLayout.GCState] != WarpPortableSchedulerLayout.GCRequested)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, epoch, 0);
        }
        if (arena[scheduler + WarpPortableSchedulerLayout.GCParkedCount] != active || arena[scheduler + WarpPortableSchedulerLayout.RunningCount] != 0 ||
            LeaseOwner(arena, scheduler) != WarpPortableSchedulerLayout.NoWorker || LeasePending(arena, scheduler) != 0)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.NeedGCParking, active, arena[scheduler + WarpPortableSchedulerLayout.GCParkedCount]);
        }
        arena[scheduler + WarpPortableSchedulerLayout.GCState] = WarpPortableSchedulerLayout.GCCollecting;
        arena[scheduler + WarpPortableSchedulerLayout.Result] = epoch;
        return 0;
    }

    public static uint FinishCollection(uint[] arena, uint scheduler, uint controller, uint epoch, uint heapStatus)
    {
        uint status = Begin(arena, scheduler, controller, 14);
        if (status != 0)
        {
            return status;
        }
        if (arena[scheduler + WarpPortableSchedulerLayout.GCState] != WarpPortableSchedulerLayout.GCCollecting ||
            epoch != arena[scheduler + WarpPortableSchedulerLayout.GCEpoch])
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, epoch, heapStatus);
        }
        if (heapStatus != 0 || (arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] != 0 &&
            (arena[WarpPortableHeapLayout.CollectionState] != WarpPortableHeapLayout.Idle || arena[WarpPortableHeapLayout.CollectionEpoch] != epoch)))
        {
            arena[scheduler + WarpPortableSchedulerLayout.GCState] = WarpPortableSchedulerLayout.GCIdle;
            if (arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] != 0)
            {
                arena[WarpPortableHeapLayout.CollectionState] = WarpPortableHeapLayout.Idle;
            }
            return RuntimeFault(arena, scheduler, 0, WarpPortableSchedulerLayout.RuntimeInvariantFault);
        }
        for (uint worker = 0; worker < arena[scheduler + WarpPortableSchedulerLayout.WorkerCount]; worker++)
        {
            uint entry = Worker(arena, scheduler, worker);
            if (arena[entry + WarpPortableSchedulerLayout.WorkerState] != WarpPortableSchedulerLayout.ParkedGC)
            {
                continue;
            }
            arena[entry + WarpPortableSchedulerLayout.WorkerState] = arena[entry + WarpPortableSchedulerLayout.ResumeState];
            if (arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] != 0)
            {
                uint heapWorker = arena[WarpPortableHeapLayout.WorkerStart] + worker * WarpPortableHeapLayout.WorkerWords;
                arena[heapWorker] = 1;
                arena[WarpPortableHeapLayout.ParkedWorkers]--;
            }
        }
        arena[scheduler + WarpPortableSchedulerLayout.GCParkedCount] = 0;
        arena[scheduler + WarpPortableSchedulerLayout.GCState] = WarpPortableSchedulerLayout.GCIdle;
        return 0;
    }
}
