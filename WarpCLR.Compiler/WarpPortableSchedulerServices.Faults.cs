namespace WarpCLR.Compiler;

internal static partial class WarpPortableSchedulerServices
{
    public static uint RecordEscapedFault(uint[] arena, uint scheduler, uint controller, uint worker, uint generation,
        uint type, uint function, uint cilOffset, uint instruction, uint frameDepth, uint objectContext, uint objectSlot, uint objectGeneration)
    {
        uint status = Begin(arena, scheduler, controller, 20);
        if (status != 0)
        {
            return status;
        }
        if (RequireRunning(arena, scheduler, worker, generation) != 0 || type == 0 ||
            frameDepth > arena[Worker(arena, scheduler, worker) + WarpPortableSchedulerLayout.UserStackLimit] ||
            (arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] != 0 &&
                (type > arena[WarpPortableHeapLayout.TypeCount] || ValidateHeapReference(arena, objectContext, objectSlot, objectGeneration) != 0)))
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, type);
        }
        StoreFault(arena, scheduler, worker, WarpPortableSchedulerLayout.ManagedFault, type, function,
            cilOffset, instruction, frameDepth, objectContext, objectSlot, objectGeneration);
        return QuarantineFault(arena, scheduler, worker);
    }

    public static uint SetUserLocation(uint[] arena, uint scheduler, uint controller, uint worker, uint generation,
        uint function, uint cilOffset, uint instruction)
    {
        uint status = Begin(arena, scheduler, controller, 25);
        if (status != 0)
        {
            return status;
        }
        if (RequireRunning(arena, scheduler, worker, generation) != 0 ||
            arena[scheduler + WarpPortableSchedulerLayout.ContextState] != WarpPortableSchedulerLayout.Active)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, generation);
        }
        uint entry = Worker(arena, scheduler, worker);
        arena[entry + WarpPortableSchedulerLayout.FaultFunction] = function;
        arena[entry + WarpPortableSchedulerLayout.FaultCilOffset] = cilOffset;
        arena[entry + WarpPortableSchedulerLayout.FaultInstruction] = instruction;
        return 0;
    }

    private static uint RuntimeFault(uint[] arena, uint scheduler, uint worker, uint kind)
    {
        uint entry = Worker(arena, scheduler, worker);
        StoreFault(arena, scheduler, worker, kind, 0, arena[entry + WarpPortableSchedulerLayout.FaultFunction],
            arena[entry + WarpPortableSchedulerLayout.FaultCilOffset], arena[entry + WarpPortableSchedulerLayout.FaultInstruction],
            arena[entry + WarpPortableSchedulerLayout.UserStackDepth], 0, 0, 0);
        return QuarantineFault(arena, scheduler, worker);
    }

    private static uint StoreFault(uint[] arena, uint scheduler, uint worker, uint kind, uint type, uint function,
        uint cilOffset, uint instruction, uint depth, uint objectContext, uint objectSlot, uint objectGeneration)
    {
        uint entry = Worker(arena, scheduler, worker);
        if (arena[entry + WarpPortableSchedulerLayout.FaultKind] != 0)
        {
            return 0;
        }
        arena[entry + WarpPortableSchedulerLayout.FaultKind] = kind;
        arena[entry + WarpPortableSchedulerLayout.FaultType] = type;
        arena[entry + WarpPortableSchedulerLayout.FaultFunction] = function;
        arena[entry + WarpPortableSchedulerLayout.FaultCilOffset] = cilOffset;
        arena[entry + WarpPortableSchedulerLayout.FaultInstruction] = instruction;
        arena[entry + WarpPortableSchedulerLayout.FaultDepth] = depth;
        arena[entry + WarpPortableSchedulerLayout.FaultObject] = objectContext;
        arena[entry + WarpPortableSchedulerLayout.FaultObject + 1] = objectSlot;
        arena[entry + WarpPortableSchedulerLayout.FaultObject + 2] = objectGeneration;
        arena[scheduler + WarpPortableSchedulerLayout.FaultCount]++;
        if (worker < arena[scheduler + WarpPortableSchedulerLayout.FaultWinner])
        {
            arena[scheduler + WarpPortableSchedulerLayout.FaultWinner] = worker;
        }
        if (arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] != 0)
        {
            uint root = arena[entry + WarpPortableSchedulerLayout.FrameRootStart] + arena[entry + WarpPortableSchedulerLayout.FrameRootCapacity];
            uint reference = arena[WarpPortableHeapLayout.RootStart] + (root - 1) * WarpPortableHeapLayout.RootWords + WarpPortableHeapLayout.RootReference;
            arena[reference] = objectContext;
            arena[reference + 1] = objectSlot;
            arena[reference + 2] = objectGeneration;
        }
        return 0;
    }

    private static uint QuarantineFault(uint[] arena, uint scheduler, uint worker)
    {
        arena[scheduler + WarpPortableSchedulerLayout.ContextState] = WarpPortableSchedulerLayout.Quarantined;
        arena[scheduler + WarpPortableSchedulerLayout.OutputQuarantined] = 1;
        if (LeaseOwner(arena, scheduler) == worker)
        {
            AbandonLease(arena, scheduler, worker);
        }
        uint entry = Worker(arena, scheduler, worker);
        if (Terminal(arena[entry + WarpPortableSchedulerLayout.WorkerState]) != 0)
        {
            arena[entry + WarpPortableSchedulerLayout.WorkerState] = WarpPortableSchedulerLayout.Faulted;
        }
        else
        {
            CompleteTerminal(arena, scheduler, worker, WarpPortableSchedulerLayout.Faulted);
        }
        StopNonrunningWorkers(arena, scheduler);
        return Status(arena, scheduler, WarpPortableSchedulerLayout.DispatchQuarantined, arena[scheduler + WarpPortableSchedulerLayout.FaultWinner], 0);
    }

    private static uint StopNonrunningWorkers(uint[] arena, uint scheduler)
    {
        for (uint barrier = 0; barrier < arena[scheduler + WarpPortableSchedulerLayout.BarrierCount]; barrier++)
        {
            uint collective = Barrier(arena, scheduler, barrier);
            arena[collective + WarpPortableSchedulerLayout.BarrierState] = WarpPortableSchedulerLayout.BarrierAborted;
            arena[collective + WarpPortableSchedulerLayout.BarrierArrived] = 0;
        }
        for (uint worker = 0; worker < arena[scheduler + WarpPortableSchedulerLayout.WorkerCount]; worker++)
        {
            uint state = arena[Worker(arena, scheduler, worker) + WarpPortableSchedulerLayout.WorkerState];
            if (state != WarpPortableSchedulerLayout.Running && Terminal(state) == 0 && LeaseOwner(arena, scheduler) != worker)
            {
                CompleteTerminal(arena, scheduler, worker, WarpPortableSchedulerLayout.Cancelled);
            }
        }
        return 0;
    }
}
