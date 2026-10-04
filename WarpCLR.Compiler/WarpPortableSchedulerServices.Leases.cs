namespace WarpCLR.Compiler;

internal static partial class WarpPortableSchedulerServices
{
    public static uint AcquireHeapService(uint[] arena, uint scheduler, uint controller, uint worker, uint generation)
    {
        uint status = Begin(arena, scheduler, controller, 15);
        if (status != 0)
        {
            return status;
        }
        if (RequireRunning(arena, scheduler, worker, generation) != 0)
        {
            return WarpPortableSchedulerLayout.Invalid;
        }
        if (arena[scheduler + WarpPortableSchedulerLayout.ContextState] != WarpPortableSchedulerLayout.Active ||
            arena[scheduler + WarpPortableSchedulerLayout.GCState] != WarpPortableSchedulerLayout.GCIdle)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Yield, worker, 0);
        }
        uint owner = LeaseOwner(arena, scheduler);
        if (owner == worker)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, owner);
        }
        if (owner != WarpPortableSchedulerLayout.NoWorker)
        {
            ReleasePhysical(arena, scheduler, worker);
            uint waiting = Worker(arena, scheduler, worker);
            arena[waiting + WarpPortableSchedulerLayout.WorkerState] = WarpPortableSchedulerLayout.WaitingService;
            arena[waiting + WarpPortableSchedulerLayout.SafePoint] = 1;
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Yield, worker, owner);
        }
        if (arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] != 0 && WarpPortableHeapServices.AcquireServiceLease(arena, worker) != 0)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, arena[WarpPortableHeapLayout.Fault]);
        }
        arena[scheduler + WarpPortableSchedulerLayout.ServiceOwner] = worker;
        arena[scheduler + WarpPortableSchedulerLayout.ServicePending] = 0;
        arena[scheduler + WarpPortableSchedulerLayout.ServiceEpoch] = arena[scheduler + WarpPortableSchedulerLayout.GCEpoch];
        arena[scheduler + WarpPortableSchedulerLayout.ServiceRootRevision] = arena[Worker(arena, scheduler, worker) + WarpPortableSchedulerLayout.RootRevision];
        return 0;
    }

    public static uint CaptureServiceResult(uint[] arena, uint scheduler, uint controller, uint worker, uint generation, uint hasReference)
    {
        uint status = Begin(arena, scheduler, controller, 16);
        if (status != 0)
        {
            return status;
        }
        if (RequireRunning(arena, scheduler, worker, generation) != 0 || LeaseOwner(arena, scheduler) != worker || hasReference > 1)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, hasReference);
        }
        if (arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] != 0 && arena[WarpPortableHeapLayout.PendingResult] != 0 && hasReference == 0)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, 0);
        }
        arena[scheduler + WarpPortableSchedulerLayout.ServicePending] = hasReference != 0 ? 1u : 2u;
        return 0;
    }

    public static uint AcknowledgeHeapResult(uint[] arena, uint scheduler, uint controller, uint worker, uint generation, uint publicationRevision)
    {
        uint status = Begin(arena, scheduler, controller, 17);
        if (status != 0)
        {
            return status;
        }
        if (RequireRunning(arena, scheduler, worker, generation) != 0 || LeaseOwner(arena, scheduler) != worker)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, generation);
        }
        if (LeasePending(arena, scheduler) != 0)
        {
            uint entry = Worker(arena, scheduler, worker);
            if (publicationRevision == 0 || publicationRevision != arena[entry + WarpPortableSchedulerLayout.RootRevision] ||
                publicationRevision <= arena[scheduler + WarpPortableSchedulerLayout.ServiceRootRevision] ||
                arena[entry + WarpPortableSchedulerLayout.RootEpoch] != arena[scheduler + WarpPortableSchedulerLayout.GCEpoch] ||
                (arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] != 0 && arena[WarpPortableHeapLayout.PendingResult] != 0 &&
                    ResultReferencePublished(arena, scheduler, entry) == 0))
            {
                return Status(arena, scheduler, WarpPortableSchedulerLayout.NeedRootPublication, worker, publicationRevision);
            }
        }
        if (arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] != 0 && WarpPortableHeapServices.AcknowledgeServiceResult(arena, worker) != 0)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, arena[WarpPortableHeapLayout.Fault]);
        }
        arena[scheduler + WarpPortableSchedulerLayout.ServicePending] = 0;
        return 0;
    }

    public static uint ReleaseHeapService(uint[] arena, uint scheduler, uint controller, uint worker, uint generation)
    {
        uint status = Begin(arena, scheduler, controller, 18);
        if (status != 0)
        {
            return status;
        }
        if (RequireRunning(arena, scheduler, worker, generation) != 0 || LeaseOwner(arena, scheduler) != worker)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, generation);
        }
        if (LeasePending(arena, scheduler) != 0)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.NeedRootPublication, worker, LeasePending(arena, scheduler));
        }
        if (arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] != 0 && WarpPortableHeapServices.ReleaseServiceLease(arena, worker) != 0)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, arena[WarpPortableHeapLayout.Fault]);
        }
        DropLease(arena, scheduler);
        return 0;
    }

    public static uint AbortHeapService(uint[] arena, uint scheduler, uint controller, uint worker, uint generation)
    {
        uint status = Begin(arena, scheduler, controller, 19);
        if (status != 0)
        {
            return status;
        }
        if (RequireWorker(arena, scheduler, worker) != 0 || LeaseOwner(arena, scheduler) != worker ||
            arena[Worker(arena, scheduler, worker) + WarpPortableSchedulerLayout.RunGeneration] != generation ||
            arena[scheduler + WarpPortableSchedulerLayout.ContextState] == WarpPortableSchedulerLayout.Active)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, generation);
        }
        AbandonLease(arena, scheduler, worker);
        return 0;
    }

    private static uint ResultReferencePublished(uint[] arena, uint scheduler, uint worker)
    {
        uint map = RootMap(arena, scheduler, arena[worker + WarpPortableSchedulerLayout.RootMap]);
        uint offsets = scheduler + arena[map + WarpPortableSchedulerLayout.MapOffsets];
        uint source = arena[worker + WarpPortableSchedulerLayout.LogicalStackBase];
        for (uint index = 0; index < arena[map + WarpPortableSchedulerLayout.MapReferenceCount]; index++)
        {
            uint value = source + arena[offsets + index];
            if (arena[value] == arena[WarpPortableHeapLayout.Result] && arena[value + 1] == arena[WarpPortableHeapLayout.Result + 1] &&
                arena[value + 2] == arena[WarpPortableHeapLayout.Result + 2])
            {
                return 1;
            }
        }
        return 0;
    }

    private static uint AbandonLease(uint[] arena, uint scheduler, uint worker)
    {
        if (arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] != 0)
        {
            WarpPortableHeapServices.AbortServiceLease(arena, worker);
        }
        DropLease(arena, scheduler);
        return 0;
    }

    private static uint DropLease(uint[] arena, uint scheduler)
    {
        arena[scheduler + WarpPortableSchedulerLayout.ServiceOwner] = WarpPortableSchedulerLayout.NoWorker;
        arena[scheduler + WarpPortableSchedulerLayout.ServicePending] = 0;
        for (uint worker = 0; worker < arena[scheduler + WarpPortableSchedulerLayout.WorkerCount]; worker++)
        {
            uint entry = Worker(arena, scheduler, worker);
            uint state = arena[entry + WarpPortableSchedulerLayout.WorkerState];
            if (state == WarpPortableSchedulerLayout.WaitingService)
            {
                if (arena[scheduler + WarpPortableSchedulerLayout.ContextState] == WarpPortableSchedulerLayout.Active)
                {
                    arena[entry + WarpPortableSchedulerLayout.WorkerState] = WarpPortableSchedulerLayout.Ready;
                }
                else
                {
                    CompleteTerminal(arena, scheduler, worker, WarpPortableSchedulerLayout.Cancelled);
                }
            }
            else if (state == WarpPortableSchedulerLayout.ParkedGC && arena[entry + WarpPortableSchedulerLayout.ResumeState] == WarpPortableSchedulerLayout.WaitingService)
            {
                if (arena[scheduler + WarpPortableSchedulerLayout.ContextState] == WarpPortableSchedulerLayout.Active)
                {
                    arena[entry + WarpPortableSchedulerLayout.ResumeState] = WarpPortableSchedulerLayout.Ready;
                }
                else
                {
                    CompleteTerminal(arena, scheduler, worker, WarpPortableSchedulerLayout.Cancelled);
                }
            }
        }
        FinishContextState(arena, scheduler);
        return 0;
    }
}
