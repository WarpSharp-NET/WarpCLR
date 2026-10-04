namespace WarpCLR.Compiler;

internal static partial class WarpPortableSchedulerServices
{
    public static uint RequestCancellation(uint[] arena, uint scheduler, uint controller)
    {
        uint status = Begin(arena, scheduler, controller, 21);
        if (status != 0)
        {
            return status;
        }
        if (arena[scheduler + WarpPortableSchedulerLayout.ContextState] == WarpPortableSchedulerLayout.CompletedContext ||
            arena[scheduler + WarpPortableSchedulerLayout.ContextState] == WarpPortableSchedulerLayout.CancelledContext)
        {
            return 0;
        }
        arena[scheduler + WarpPortableSchedulerLayout.CancelRequested] = 1;
        arena[scheduler + WarpPortableSchedulerLayout.OutputQuarantined] = 1;
        if (arena[scheduler + WarpPortableSchedulerLayout.ContextState] != WarpPortableSchedulerLayout.Quarantined)
        {
            arena[scheduler + WarpPortableSchedulerLayout.ContextState] = WarpPortableSchedulerLayout.Cancelling;
        }
        StopNonrunningWorkers(arena, scheduler);
        FinishContextState(arena, scheduler);
        return 0;
    }

    public static uint CancelWorker(uint[] arena, uint scheduler, uint controller, uint worker, uint generation)
    {
        uint status = Begin(arena, scheduler, controller, 22);
        if (status != 0)
        {
            return status;
        }
        if (RequireRunning(arena, scheduler, worker, generation) != 0 ||
            arena[scheduler + WarpPortableSchedulerLayout.ContextState] == WarpPortableSchedulerLayout.Active)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, generation);
        }
        if (LeaseOwner(arena, scheduler) == worker)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Yield, worker, LeasePending(arena, scheduler));
        }
        CompleteTerminal(arena, scheduler, worker, WarpPortableSchedulerLayout.Cancelled);
        return 0;
    }

    public static uint RequestDisposal(uint[] arena, uint scheduler, uint controller)
    {
        uint status = Begin(arena, scheduler, controller, 23);
        if (status != 0)
        {
            return status;
        }
        arena[scheduler + WarpPortableSchedulerLayout.DisposeRequested] = 1;
        arena[scheduler + WarpPortableSchedulerLayout.CancelRequested] = 1;
        arena[scheduler + WarpPortableSchedulerLayout.OutputQuarantined] = 1;
        arena[scheduler + WarpPortableSchedulerLayout.ContextState] = WarpPortableSchedulerLayout.Disposing;
        StopNonrunningWorkers(arena, scheduler);
        return 0;
    }

    public static uint FinishDisposal(uint[] arena, uint scheduler, uint controller)
    {
        uint status = Begin(arena, scheduler, controller, 24);
        if (status != 0)
        {
            return status;
        }
        if (arena[scheduler + WarpPortableSchedulerLayout.DisposeRequested] == 0)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, 0, 0);
        }
        if (arena[scheduler + WarpPortableSchedulerLayout.RunningCount] != 0 || LeaseOwner(arena, scheduler) != WarpPortableSchedulerLayout.NoWorker ||
            LeasePending(arena, scheduler) != 0 || arena[scheduler + WarpPortableSchedulerLayout.GCState] == WarpPortableSchedulerLayout.GCCollecting ||
            arena[scheduler + WarpPortableSchedulerLayout.TerminalCount] != arena[scheduler + WarpPortableSchedulerLayout.WorkerCount])
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Yield, 0, 0);
        }
        for (uint worker = 0; worker < arena[scheduler + WarpPortableSchedulerLayout.WorkerCount]; worker++)
        {
            uint entry = Worker(arena, scheduler, worker);
            arena[entry + WarpPortableSchedulerLayout.WorkerState] = WarpPortableSchedulerLayout.Disposed;
            arena[entry + WarpPortableSchedulerLayout.RootEpoch] = 0;
            arena[entry + WarpPortableSchedulerLayout.RootRevision] = 0;
            CopyOutputRoots(arena, scheduler, entry, 1);
            arena[entry + WarpPortableSchedulerLayout.OutputState] = WarpPortableSchedulerLayout.NoOutput;
            for (uint word = 0; word < 3; word++)
            {
                arena[entry + WarpPortableSchedulerLayout.FaultObject + word] = 0;
            }
            if (arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] != 0)
            {
                uint root = arena[entry + WarpPortableSchedulerLayout.FrameRootStart] + arena[entry + WarpPortableSchedulerLayout.FrameRootCapacity];
                uint reference = arena[WarpPortableHeapLayout.RootStart] + (root - 1) * WarpPortableHeapLayout.RootWords + WarpPortableHeapLayout.RootReference;
                arena[reference] = 0;
                arena[reference + 1] = 0;
                arena[reference + 2] = 0;
            }
        }
        arena[scheduler + WarpPortableSchedulerLayout.OutputPendingWorkers] = 0;
        arena[scheduler + WarpPortableSchedulerLayout.ContextState] = WarpPortableSchedulerLayout.DisposedContext;
        return 0;
    }
}
