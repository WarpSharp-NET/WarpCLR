namespace WarpCLR.Compiler;

internal static partial class WarpPortableSchedulerServices
{
    public static uint PublishOutputRoots(uint[] arena, uint scheduler, uint controller, uint worker, uint generation, uint dispatch)
    {
        uint status = Begin(arena, scheduler, controller, 26);
        if (status != 0)
        {
            return status;
        }
        if (RequireRunning(arena, scheduler, worker, generation) != 0 || dispatch != arena[scheduler + WarpPortableSchedulerLayout.DispatchGeneration] ||
            arena[scheduler + WarpPortableSchedulerLayout.ContextState] != WarpPortableSchedulerLayout.Active)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, dispatch);
        }
        if (LeaseOwner(arena, scheduler) == worker)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Yield, worker, 0);
        }
        if (arena[scheduler + WarpPortableSchedulerLayout.OutputReferenceCount] == 0)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, dispatch);
        }
        uint entry = Worker(arena, scheduler, worker);
        uint output = arena[entry + WarpPortableSchedulerLayout.OutputState];
        if (output != WarpPortableSchedulerLayout.NoOutput)
        {
            if (arena[entry + WarpPortableSchedulerLayout.OutputGeneration] == dispatch)
            {
                return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, dispatch);
            }
            ReleasePhysical(arena, scheduler, worker);
            arena[entry + WarpPortableSchedulerLayout.WorkerState] = WarpPortableSchedulerLayout.WaitingOutput;
            arena[entry + WarpPortableSchedulerLayout.SafePoint] = 1;
            return Status(arena, scheduler, WarpPortableSchedulerLayout.NeedOutputRelease, worker, arena[entry + WarpPortableSchedulerLayout.OutputGeneration]);
        }
        if (ValidateOutputReferences(arena, scheduler, entry) != 0)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, dispatch);
        }
        CopyOutputRoots(arena, scheduler, entry, 0);
        arena[entry + WarpPortableSchedulerLayout.OutputState] = WarpPortableSchedulerLayout.OutputPublished;
        arena[entry + WarpPortableSchedulerLayout.OutputGeneration] = dispatch;
        arena[scheduler + WarpPortableSchedulerLayout.OutputPendingWorkers]++;
        return 0;
    }

    public static uint AcknowledgeOutputRoots(uint[] arena, uint scheduler, uint controller, uint worker, uint generation, uint dispatch)
    {
        uint status = Begin(arena, scheduler, controller, 27);
        if (status != 0)
        {
            return status;
        }
        if (RequireRunning(arena, scheduler, worker, generation) != 0)
        {
            return WarpPortableSchedulerLayout.Invalid;
        }
        uint entry = Worker(arena, scheduler, worker);
        if (dispatch != arena[scheduler + WarpPortableSchedulerLayout.DispatchGeneration] ||
            arena[entry + WarpPortableSchedulerLayout.OutputGeneration] != dispatch ||
            arena[entry + WarpPortableSchedulerLayout.OutputState] != WarpPortableSchedulerLayout.OutputPublished)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, dispatch);
        }
        arena[entry + WarpPortableSchedulerLayout.OutputState] = WarpPortableSchedulerLayout.OutputAcknowledged;
        return 0;
    }

    public static uint ReleaseOutputRoots(uint[] arena, uint scheduler, uint controller, uint worker, uint dispatch)
    {
        uint status = Begin(arena, scheduler, controller, 28);
        if (status != 0)
        {
            return status;
        }
        if (RequireWorker(arena, scheduler, worker) != 0)
        {
            return WarpPortableSchedulerLayout.Invalid;
        }
        uint entry = Worker(arena, scheduler, worker);
        uint output = arena[entry + WarpPortableSchedulerLayout.OutputState];
        if (dispatch == 0 || arena[entry + WarpPortableSchedulerLayout.OutputGeneration] != dispatch ||
            (output != WarpPortableSchedulerLayout.OutputAcknowledged && output != WarpPortableSchedulerLayout.OutputInherited) ||
            (output == WarpPortableSchedulerLayout.OutputAcknowledged && Terminal(arena[entry + WarpPortableSchedulerLayout.WorkerState]) == 0))
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, dispatch);
        }
        CopyOutputRoots(arena, scheduler, entry, 1);
        arena[entry + WarpPortableSchedulerLayout.OutputState] = WarpPortableSchedulerLayout.NoOutput;
        arena[scheduler + WarpPortableSchedulerLayout.OutputPendingWorkers]--;
        if (arena[entry + WarpPortableSchedulerLayout.WorkerState] == WarpPortableSchedulerLayout.WaitingOutput)
        {
            arena[entry + WarpPortableSchedulerLayout.WorkerState] = WarpPortableSchedulerLayout.Ready;
        }
        else if (arena[entry + WarpPortableSchedulerLayout.WorkerState] == WarpPortableSchedulerLayout.ParkedGC &&
            arena[entry + WarpPortableSchedulerLayout.ResumeState] == WarpPortableSchedulerLayout.WaitingOutput)
        {
            arena[entry + WarpPortableSchedulerLayout.ResumeState] = WarpPortableSchedulerLayout.Ready;
        }
        return 0;
    }

    private static uint ValidateOutputReferences(uint[] arena, uint scheduler, uint worker)
    {
        uint count = arena[scheduler + WarpPortableSchedulerLayout.OutputReferenceCount];
        uint offsets = scheduler + arena[scheduler + WarpPortableSchedulerLayout.OutputOffsets];
        uint source = arena[worker + WarpPortableSchedulerLayout.LogicalStackBase];
        uint quota = arena[scheduler + WarpPortableSchedulerLayout.LogicalStateWords];
        for (uint index = 0; index < count; index++)
        {
            uint offset = arena[offsets + index];
            if (quota < 3 || offset > quota - 3 ||
                (arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] != 0 &&
                    ValidateHeapReference(arena, arena[source + offset], arena[source + offset + 1], arena[source + offset + 2]) != 0))
            {
                return WarpPortableSchedulerLayout.Invalid;
            }
        }
        return 0;
    }

    private static uint CopyOutputRoots(uint[] arena, uint scheduler, uint worker, uint clear)
    {
        if (arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] == 0)
        {
            return 0;
        }
        uint count = arena[scheduler + WarpPortableSchedulerLayout.OutputReferenceCount];
        uint offsets = scheduler + arena[scheduler + WarpPortableSchedulerLayout.OutputOffsets];
        uint source = arena[worker + WarpPortableSchedulerLayout.LogicalStackBase];
        uint root = arena[worker + WarpPortableSchedulerLayout.FrameRootStart] + arena[worker + WarpPortableSchedulerLayout.FrameRootCapacity] + 1;
        for (uint index = 0; index < count; index++)
        {
            uint reference = arena[WarpPortableHeapLayout.RootStart] + (root + index - 1) * WarpPortableHeapLayout.RootWords + WarpPortableHeapLayout.RootReference;
            uint value = source + arena[offsets + index];
            arena[reference] = clear == 0 ? arena[value] : 0;
            arena[reference + 1] = clear == 0 ? arena[value + 1] : 0;
            arena[reference + 2] = clear == 0 ? arena[value + 2] : 0;
        }
        return 0;
    }
}
