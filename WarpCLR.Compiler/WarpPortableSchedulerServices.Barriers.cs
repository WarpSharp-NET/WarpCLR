namespace WarpCLR.Compiler;

internal static partial class WarpPortableSchedulerServices
{
    public static uint ArriveBarrier(uint[] arena, uint scheduler, uint controller, uint worker,
        uint generation, uint barrier, uint barrierGeneration, uint site)
    {
        uint status = Begin(arena, scheduler, controller, 8);
        if (status != 0)
        {
            return status;
        }
        if (RequireRunning(arena, scheduler, worker, generation) != 0 ||
            barrier >= arena[scheduler + WarpPortableSchedulerLayout.BarrierCount])
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, barrier);
        }
        if (arena[scheduler + WarpPortableSchedulerLayout.ContextState] != WarpPortableSchedulerLayout.Active)
        {
            return ProgressStatus(arena, scheduler);
        }
        if (LeaseOwner(arena, scheduler) == worker)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Yield, worker, LeasePending(arena, scheduler));
        }
        uint collective = Barrier(arena, scheduler, barrier);
        uint members = scheduler + arena[collective + WarpPortableSchedulerLayout.BarrierMembership];
        if (arena[members + worker] == 0 || site != arena[collective + WarpPortableSchedulerLayout.BarrierSite] ||
            barrierGeneration != arena[collective + WarpPortableSchedulerLayout.BarrierGeneration] ||
            arena[collective + WarpPortableSchedulerLayout.BarrierState] == WarpPortableSchedulerLayout.BarrierAborted)
        {
            return RuntimeFault(arena, scheduler, worker, WarpPortableSchedulerLayout.CollectiveViolation);
        }
        for (uint peer = 0; peer < arena[scheduler + WarpPortableSchedulerLayout.WorkerCount]; peer++)
        {
            if (arena[members + peer] != 0 && Terminal(arena[Worker(arena, scheduler, peer) + WarpPortableSchedulerLayout.WorkerState]) != 0)
            {
                return RuntimeFault(arena, scheduler, worker, WarpPortableSchedulerLayout.CollectiveViolation);
            }
        }
        uint entry = Worker(arena, scheduler, worker);
        ReleasePhysical(arena, scheduler, worker);
        arena[entry + WarpPortableSchedulerLayout.WorkerState] = WarpPortableSchedulerLayout.ParkedBarrier;
        arena[entry + WarpPortableSchedulerLayout.WaitBarrier] = barrier;
        arena[entry + WarpPortableSchedulerLayout.WaitGeneration] = barrierGeneration;
        arena[entry + WarpPortableSchedulerLayout.SafePoint] = 1;
        arena[collective + WarpPortableSchedulerLayout.BarrierState] = WarpPortableSchedulerLayout.BarrierWaiting;
        arena[collective + WarpPortableSchedulerLayout.BarrierArrived]++;
        if (arena[collective + WarpPortableSchedulerLayout.BarrierArrived] == arena[collective + WarpPortableSchedulerLayout.BarrierExpected])
        {
            if (barrierGeneration == 0xFFFFFFFFu)
            {
                return RuntimeFault(arena, scheduler, worker, WarpPortableSchedulerLayout.GenerationExhausted);
            }
            ReleaseBarrier(arena, scheduler, barrier, barrierGeneration);
        }
        return 0;
    }

    public static uint DetectStalledCollective(uint[] arena, uint scheduler, uint controller)
    {
        uint status = Begin(arena, scheduler, controller, 9);
        return status != 0 ? status : ProgressStatus(arena, scheduler);
    }

    private static uint ReleaseBarrier(uint[] arena, uint scheduler, uint barrier, uint generation)
    {
        uint collective = Barrier(arena, scheduler, barrier);
        arena[collective + WarpPortableSchedulerLayout.BarrierGeneration] = generation + 1;
        arena[collective + WarpPortableSchedulerLayout.BarrierArrived] = 0;
        arena[collective + WarpPortableSchedulerLayout.BarrierState] = WarpPortableSchedulerLayout.BarrierIdle;
        for (uint worker = 0; worker < arena[scheduler + WarpPortableSchedulerLayout.WorkerCount]; worker++)
        {
            uint entry = Worker(arena, scheduler, worker);
            if (arena[entry + WarpPortableSchedulerLayout.WaitBarrier] != barrier ||
                arena[entry + WarpPortableSchedulerLayout.WaitGeneration] != generation)
            {
                continue;
            }
            arena[entry + WarpPortableSchedulerLayout.WaitGeneration] = 0;
            if (arena[entry + WarpPortableSchedulerLayout.WorkerState] == WarpPortableSchedulerLayout.ParkedBarrier)
            {
                arena[entry + WarpPortableSchedulerLayout.WorkerState] = WarpPortableSchedulerLayout.Ready;
            }
            else if (arena[entry + WarpPortableSchedulerLayout.WorkerState] == WarpPortableSchedulerLayout.ParkedGC &&
                arena[entry + WarpPortableSchedulerLayout.ResumeState] == WarpPortableSchedulerLayout.ParkedBarrier)
            {
                arena[entry + WarpPortableSchedulerLayout.ResumeState] = WarpPortableSchedulerLayout.Ready;
            }
        }
        return 0;
    }

    private static uint ProgressStatus(uint[] arena, uint scheduler)
    {
        FinishContextState(arena, scheduler);
        uint context = arena[scheduler + WarpPortableSchedulerLayout.ContextState];
        if (context == WarpPortableSchedulerLayout.Quarantined)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.DispatchQuarantined, arena[scheduler + WarpPortableSchedulerLayout.FaultWinner], 0);
        }
        if (context == WarpPortableSchedulerLayout.CompletedContext)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.DispatchCompleted, 0, 0);
        }
        if (context != WarpPortableSchedulerLayout.Active)
        {
            return Status(arena, scheduler, context == WarpPortableSchedulerLayout.DisposedContext ?
                WarpPortableSchedulerLayout.ContextDisposed : WarpPortableSchedulerLayout.DispatchCancelled, 0, 0);
        }
        if (arena[scheduler + WarpPortableSchedulerLayout.GCState] != WarpPortableSchedulerLayout.GCIdle)
        {
            uint active = arena[scheduler + WarpPortableSchedulerLayout.WorkerCount] - arena[scheduler + WarpPortableSchedulerLayout.TerminalCount];
            return Status(arena, scheduler,
                arena[scheduler + WarpPortableSchedulerLayout.GCParkedCount] == active && LeaseOwner(arena, scheduler) == WarpPortableSchedulerLayout.NoWorker &&
                    arena[scheduler + WarpPortableSchedulerLayout.RunningCount] == 0 ? WarpPortableSchedulerLayout.CollectionReady :
                    WarpPortableSchedulerLayout.NeedGCParking, arena[scheduler + WarpPortableSchedulerLayout.GCEpoch], 0);
        }
        if (arena[scheduler + WarpPortableSchedulerLayout.RunningCount] != 0 || LeaseOwner(arena, scheduler) != WarpPortableSchedulerLayout.NoWorker)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Yield, 0, 0);
        }
        uint waiting = WarpPortableSchedulerLayout.NoWorker;
        uint output = WarpPortableSchedulerLayout.NoWorker;
        for (uint worker = 0; worker < arena[scheduler + WarpPortableSchedulerLayout.WorkerCount]; worker++)
        {
            uint state = arena[Worker(arena, scheduler, worker) + WarpPortableSchedulerLayout.WorkerState];
            if (state == WarpPortableSchedulerLayout.Ready)
            {
                return Status(arena, scheduler, WarpPortableSchedulerLayout.Success, worker, 0);
            }
            if (state == WarpPortableSchedulerLayout.WaitingOutput && output == WarpPortableSchedulerLayout.NoWorker)
            {
                output = worker;
            }
            if (Terminal(state) == 0 && waiting == WarpPortableSchedulerLayout.NoWorker)
            {
                waiting = worker;
            }
        }
        if (output != WarpPortableSchedulerLayout.NoWorker)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.NeedOutputRelease, output,
                arena[Worker(arena, scheduler, output) + WarpPortableSchedulerLayout.OutputGeneration]);
        }
        return waiting == WarpPortableSchedulerLayout.NoWorker ?
            Status(arena, scheduler, WarpPortableSchedulerLayout.NoReadyWorker, 0, 0) :
            RuntimeFault(arena, scheduler, waiting, WarpPortableSchedulerLayout.CollectiveViolation);
    }
}
