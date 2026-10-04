namespace WarpCLR.Compiler;

internal static partial class WarpPortableSchedulerServices
{
    public static uint TryAcquireWorker(uint[] arena, uint scheduler, uint controller, uint physical)
    {
        uint status = Begin(arena, scheduler, controller, 1);
        if (status != 0)
        {
            return status;
        }
        if (physical >= arena[scheduler + WarpPortableSchedulerLayout.ResidentCount])
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, physical, 0);
        }
        uint resident = Physical(arena, scheduler, physical);
        if (arena[resident + WarpPortableSchedulerLayout.PhysicalState] != 0)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Yield, physical, 0);
        }
        uint count = arena[scheduler + WarpPortableSchedulerLayout.WorkerCount];
        uint worker = arena[scheduler + WarpPortableSchedulerLayout.Cursor];
        for (uint visited = 0; visited < count; visited++)
        {
            uint entry = Worker(arena, scheduler, worker);
            uint ordinary = arena[scheduler + WarpPortableSchedulerLayout.ContextState] == WarpPortableSchedulerLayout.Active &&
                arena[scheduler + WarpPortableSchedulerLayout.GCState] == WarpPortableSchedulerLayout.GCIdle ? 1u : 0u;
            if (arena[entry + WarpPortableSchedulerLayout.WorkerState] == WarpPortableSchedulerLayout.Ready &&
                (ordinary != 0 || LeaseOwner(arena, scheduler) == worker))
            {
                if (arena[entry + WarpPortableSchedulerLayout.RunGeneration] == 0xFFFFFFFFu)
                {
                    return RuntimeFault(arena, scheduler, worker, WarpPortableSchedulerLayout.GenerationExhausted);
                }
                uint generation = arena[entry + WarpPortableSchedulerLayout.RunGeneration] + 1;
                arena[entry + WarpPortableSchedulerLayout.RunGeneration] = generation;
                arena[entry + WarpPortableSchedulerLayout.WorkerState] = WarpPortableSchedulerLayout.Running;
                arena[entry + WarpPortableSchedulerLayout.PhysicalOwner] = physical;
                arena[entry + WarpPortableSchedulerLayout.QuantumRemaining] = arena[scheduler + WarpPortableSchedulerLayout.Quantum];
                arena[entry + WarpPortableSchedulerLayout.SafePoint] = 0;
                arena[resident + WarpPortableSchedulerLayout.PhysicalState] = 1;
                arena[resident + WarpPortableSchedulerLayout.PhysicalWorker] = worker;
                arena[resident + WarpPortableSchedulerLayout.PhysicalGeneration] = generation;
                arena[scheduler + WarpPortableSchedulerLayout.RunningCount]++;
                arena[scheduler + WarpPortableSchedulerLayout.Cursor] = worker + 1 == count ? 0 : worker + 1;
                arena[scheduler + WarpPortableSchedulerLayout.Result] = worker;
                arena[scheduler + WarpPortableSchedulerLayout.Result + 1] = generation;
                arena[scheduler + WarpPortableSchedulerLayout.Result + 2] = ordinary != 0 ?
                    WarpPortableSchedulerLayout.OrdinaryContinuation : WarpPortableSchedulerLayout.RuntimeContinuation;
                arena[scheduler + WarpPortableSchedulerLayout.Result + 3] = arena[scheduler + WarpPortableSchedulerLayout.Quantum];
                return 0;
            }
            worker++;
            if (worker == count)
            {
                worker = 0;
            }
        }
        return ProgressStatus(arena, scheduler);
    }

    public static uint ChargeQuantum(uint[] arena, uint scheduler, uint controller, uint worker, uint generation, uint cost)
    {
        uint status = Begin(arena, scheduler, controller, 2);
        if (status != 0)
        {
            return status;
        }
        if (RequireRunning(arena, scheduler, worker, generation) != 0 || cost == 0)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, cost);
        }
        uint entry = Worker(arena, scheduler, worker);
        if (cost > arena[entry + WarpPortableSchedulerLayout.QuantumRemaining])
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Yield, worker, generation);
        }
        arena[entry + WarpPortableSchedulerLayout.QuantumRemaining] -= cost;
        return 0;
    }

    public static uint YieldWorker(uint[] arena, uint scheduler, uint controller, uint worker, uint generation)
    {
        uint status = Begin(arena, scheduler, controller, 3);
        if (status != 0)
        {
            return status;
        }
        if (RequireRunning(arena, scheduler, worker, generation) != 0)
        {
            return WarpPortableSchedulerLayout.Invalid;
        }
        if (arena[scheduler + WarpPortableSchedulerLayout.ContextState] != WarpPortableSchedulerLayout.Active &&
            LeaseOwner(arena, scheduler) != worker)
        {
            CompleteTerminal(arena, scheduler, worker, WarpPortableSchedulerLayout.Cancelled);
            return ProgressStatus(arena, scheduler);
        }
        ReleasePhysical(arena, scheduler, worker);
        uint entry = Worker(arena, scheduler, worker);
        arena[entry + WarpPortableSchedulerLayout.WorkerState] = WarpPortableSchedulerLayout.Ready;
        arena[entry + WarpPortableSchedulerLayout.SafePoint] = 1;
        return 0;
    }

    public static uint CompleteWorker(uint[] arena, uint scheduler, uint controller, uint worker, uint generation)
    {
        uint status = Begin(arena, scheduler, controller, 4);
        if (status != 0)
        {
            return status;
        }
        if (RequireRunning(arena, scheduler, worker, generation) != 0)
        {
            return WarpPortableSchedulerLayout.Invalid;
        }
        if (LeaseOwner(arena, scheduler) == worker)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Yield, worker, LeasePending(arena, scheduler));
        }
        if (arena[scheduler + WarpPortableSchedulerLayout.ContextState] != WarpPortableSchedulerLayout.Active)
        {
            CompleteTerminal(arena, scheduler, worker, WarpPortableSchedulerLayout.Cancelled);
            return ProgressStatus(arena, scheduler);
        }
        uint entry = Worker(arena, scheduler, worker);
        if (arena[scheduler + WarpPortableSchedulerLayout.OutputReferenceCount] != 0 &&
            (arena[entry + WarpPortableSchedulerLayout.OutputState] != WarpPortableSchedulerLayout.OutputAcknowledged ||
                arena[entry + WarpPortableSchedulerLayout.OutputGeneration] != arena[scheduler + WarpPortableSchedulerLayout.DispatchGeneration]))
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.NeedRootPublication, worker, 0);
        }
        for (uint barrier = 0; barrier < arena[scheduler + WarpPortableSchedulerLayout.BarrierCount]; barrier++)
        {
            uint collective = Barrier(arena, scheduler, barrier);
            uint members = scheduler + arena[collective + WarpPortableSchedulerLayout.BarrierMembership];
            if (arena[collective + WarpPortableSchedulerLayout.BarrierState] == WarpPortableSchedulerLayout.BarrierWaiting &&
                arena[members + worker] != 0)
            {
                return RuntimeFault(arena, scheduler, worker, WarpPortableSchedulerLayout.CollectiveViolation);
            }
        }
        CompleteTerminal(arena, scheduler, worker, WarpPortableSchedulerLayout.Completed);
        return 0;
    }

    public static uint ChargeUserSteps(uint[] arena, uint scheduler, uint controller, uint worker, uint generation, uint steps)
    {
        uint status = Begin(arena, scheduler, controller, 5);
        if (status != 0)
        {
            return status;
        }
        if (RequireRunning(arena, scheduler, worker, generation) != 0 || steps == 0)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, steps);
        }
        if (arena[scheduler + WarpPortableSchedulerLayout.ContextState] != WarpPortableSchedulerLayout.Active)
        {
            return ProgressStatus(arena, scheduler);
        }
        if (arena[scheduler + WarpPortableSchedulerLayout.GCState] != WarpPortableSchedulerLayout.GCIdle)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Yield, worker, 0);
        }
        uint entry = Worker(arena, scheduler, worker);
        uint low = arena[entry + WarpPortableSchedulerLayout.StepRemainingLow];
        uint high = arena[entry + WarpPortableSchedulerLayout.StepRemainingHigh];
        if (high == 0 && low < steps)
        {
            return RuntimeFault(arena, scheduler, worker, WarpPortableSchedulerLayout.StepsExhausted);
        }
        arena[entry + WarpPortableSchedulerLayout.StepRemainingLow] = low - steps;
        arena[entry + WarpPortableSchedulerLayout.StepRemainingHigh] = low < steps ? high - 1 : high;
        uint spent = arena[entry + WarpPortableSchedulerLayout.StepSpentLow];
        uint next = spent + steps;
        arena[entry + WarpPortableSchedulerLayout.StepSpentLow] = next;
        arena[entry + WarpPortableSchedulerLayout.StepSpentHigh] += next < spent ? 1u : 0u;
        return 0;
    }

    public static uint EnterUserFrame(uint[] arena, uint scheduler, uint controller, uint worker, uint generation)
    {
        uint status = Begin(arena, scheduler, controller, 6);
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
        uint entry = Worker(arena, scheduler, worker);
        if (arena[entry + WarpPortableSchedulerLayout.UserStackDepth] >= arena[entry + WarpPortableSchedulerLayout.UserStackLimit])
        {
            return RuntimeFault(arena, scheduler, worker, WarpPortableSchedulerLayout.StackExhausted);
        }
        arena[entry + WarpPortableSchedulerLayout.UserStackDepth]++;
        return 0;
    }

    public static uint ExitUserFrame(uint[] arena, uint scheduler, uint controller, uint worker, uint generation)
    {
        uint status = Begin(arena, scheduler, controller, 7);
        if (status != 0)
        {
            return status;
        }
        if (RequireRunning(arena, scheduler, worker, generation) != 0)
        {
            return WarpPortableSchedulerLayout.Invalid;
        }
        uint entry = Worker(arena, scheduler, worker);
        if (arena[entry + WarpPortableSchedulerLayout.UserStackDepth] == 0)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, 0);
        }
        arena[entry + WarpPortableSchedulerLayout.UserStackDepth]--;
        return 0;
    }
}
