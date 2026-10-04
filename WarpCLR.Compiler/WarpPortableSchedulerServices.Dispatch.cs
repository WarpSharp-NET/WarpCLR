namespace WarpCLR.Compiler;

internal static partial class WarpPortableSchedulerServices
{
    public static uint BeginDispatch(uint[] arena, uint scheduler, uint controller)
    {
        uint status = Begin(arena, scheduler, controller, 29);
        if (status != 0)
        {
            return status;
        }
        uint count = arena[scheduler + WarpPortableSchedulerLayout.WorkerCount];
        if (arena[scheduler + WarpPortableSchedulerLayout.ContextState] != WarpPortableSchedulerLayout.CompletedContext ||
            arena[scheduler + WarpPortableSchedulerLayout.TerminalCount] != count || arena[scheduler + WarpPortableSchedulerLayout.RunningCount] != 0 ||
            arena[scheduler + WarpPortableSchedulerLayout.GCState] != WarpPortableSchedulerLayout.GCIdle ||
            LeaseOwner(arena, scheduler) != WarpPortableSchedulerLayout.NoWorker || LeasePending(arena, scheduler) != 0)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, 0, 0);
        }
        if (arena[scheduler + WarpPortableSchedulerLayout.DispatchGeneration] == 0xFFFFFFFFu)
        {
            return RuntimeFault(arena, scheduler, 0, WarpPortableSchedulerLayout.GenerationExhausted);
        }
        for (uint barrier = 0; barrier < arena[scheduler + WarpPortableSchedulerLayout.BarrierCount]; barrier++)
        {
            if (arena[Barrier(arena, scheduler, barrier) + WarpPortableSchedulerLayout.BarrierGeneration] == 0xFFFFFFFFu)
            {
                return RuntimeFault(arena, scheduler, 0, WarpPortableSchedulerLayout.GenerationExhausted);
            }
        }
        for (uint barrier = 0; barrier < arena[scheduler + WarpPortableSchedulerLayout.BarrierCount]; barrier++)
        {
            uint collective = Barrier(arena, scheduler, barrier);
            arena[collective + WarpPortableSchedulerLayout.BarrierGeneration]++;
            arena[collective + WarpPortableSchedulerLayout.BarrierArrived] = 0;
            arena[collective + WarpPortableSchedulerLayout.BarrierState] = WarpPortableSchedulerLayout.BarrierIdle;
        }
        for (uint worker = 0; worker < count; worker++)
        {
            ResetDispatchWorker(arena, scheduler, worker);
        }
        arena[scheduler + WarpPortableSchedulerLayout.DispatchGeneration]++;
        arena[scheduler + WarpPortableSchedulerLayout.ContextState] = WarpPortableSchedulerLayout.Active;
        arena[scheduler + WarpPortableSchedulerLayout.Cursor] = 0;
        arena[scheduler + WarpPortableSchedulerLayout.TerminalCount] = 0;
        arena[scheduler + WarpPortableSchedulerLayout.CancelRequested] = 0;
        arena[scheduler + WarpPortableSchedulerLayout.OutputQuarantined] = 0;
        arena[scheduler + WarpPortableSchedulerLayout.FaultWinner] = WarpPortableSchedulerLayout.NoWorker;
        arena[scheduler + WarpPortableSchedulerLayout.FaultCount] = 0;
        if (arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] != 0)
        {
            arena[WarpPortableHeapLayout.ActiveWorkers] = count;
        }
        return 0;
    }

    private static uint ResetDispatchWorker(uint[] arena, uint scheduler, uint worker)
    {
        uint entry = Worker(arena, scheduler, worker);
        arena[entry + WarpPortableSchedulerLayout.WorkerState] = WarpPortableSchedulerLayout.Ready;
        arena[entry + WarpPortableSchedulerLayout.PhysicalOwner] = WarpPortableSchedulerLayout.NoWorker;
        arena[entry + WarpPortableSchedulerLayout.QuantumRemaining] = 0;
        arena[entry + WarpPortableSchedulerLayout.RootMap] = 0;
        arena[entry + WarpPortableSchedulerLayout.ContinuationFunction] = arena[RootMap(arena, scheduler, 0) + WarpPortableSchedulerLayout.MapFunction];
        arena[entry + WarpPortableSchedulerLayout.ContinuationPC] = arena[RootMap(arena, scheduler, 0) + WarpPortableSchedulerLayout.MapPC];
        arena[entry + WarpPortableSchedulerLayout.StepRemainingLow] = arena[entry + WarpPortableSchedulerLayout.InitialStepLow];
        arena[entry + WarpPortableSchedulerLayout.StepRemainingHigh] = arena[entry + WarpPortableSchedulerLayout.InitialStepHigh];
        arena[entry + WarpPortableSchedulerLayout.StepSpentLow] = 0;
        arena[entry + WarpPortableSchedulerLayout.StepSpentHigh] = 0;
        arena[entry + WarpPortableSchedulerLayout.UserStackDepth] = 1;
        arena[entry + WarpPortableSchedulerLayout.UserStackLimit] = arena[entry + WarpPortableSchedulerLayout.InitialStackLimit];
        arena[entry + WarpPortableSchedulerLayout.WaitBarrier] = 0;
        arena[entry + WarpPortableSchedulerLayout.WaitGeneration] = 0;
        arena[entry + WarpPortableSchedulerLayout.ResumeState] = 0;
        arena[entry + WarpPortableSchedulerLayout.RootLiveWords] = 0;
        arena[entry + WarpPortableSchedulerLayout.SafePoint] = 1;
        if (arena[entry + WarpPortableSchedulerLayout.OutputState] == WarpPortableSchedulerLayout.OutputAcknowledged)
        {
            arena[entry + WarpPortableSchedulerLayout.OutputState] = WarpPortableSchedulerLayout.OutputInherited;
        }
        uint source = arena[entry + WarpPortableSchedulerLayout.LogicalStackBase];
        for (uint word = 0; word < arena[scheduler + WarpPortableSchedulerLayout.LogicalStateWords]; word++)
        {
            arena[source + word] = 0;
        }
        for (uint word = WarpPortableSchedulerLayout.FaultKind; word <= WarpPortableSchedulerLayout.FaultInstruction; word++)
        {
            arena[entry + word] = 0;
        }
        if (arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] != 0)
        {
            arena[arena[WarpPortableHeapLayout.WorkerStart] + worker * WarpPortableHeapLayout.WorkerWords] = 1;
        }
        return 0;
    }
}
