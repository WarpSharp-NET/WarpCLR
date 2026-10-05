namespace WarpCLR.Compiler;

internal static partial class WarpPortableExceptionServices
{
    public static uint DiscardPrepared(uint[] arena, uint controller, uint worker, uint run, uint dispatch)
    {
        uint fault = Begin(arena, controller, worker, run, dispatch);
        if (fault != 0) { return fault; }
        uint descriptor = arena[WarpPortableExceptionLayout.Descriptor]; uint entry = Worker(arena, descriptor, worker);
        uint index = arena[entry + WarpPortableExceptionLayout.PreparedRecord];
        if (index == 0 || index > arena[descriptor + WarpPortableExceptionLayout.RecordsPerWorker] ||
            arena[Record(arena, descriptor, worker, index) + WarpPortableExceptionLayout.Phase] != WarpPortableExceptionLayout.Prepared) { return WarpPortableExceptionLayout.InvalidPhase; }
        fault = ClearRecord(arena, descriptor, Record(arena, descriptor, worker, index));
        if (fault == 0) { arena[entry + WarpPortableExceptionLayout.PreparedRecord] = 0; }
        return fault;
    }

    // A generated completed-only BeginDispatch must already have reset every
    // worker. This never disposes host/output roots or rewinds trace generations.
    public static uint ResetForDispatch(uint[] arena, uint controller, uint previousDispatch)
    {
        uint descriptor = Descriptor(arena);
        if (descriptor == 0) { return WarpPortableExceptionLayout.InvalidDescriptor; }
        uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
        if (ValidLifetimeController(arena, scheduler, controller) == 0 || previousDispatch == 0 || previousDispatch == uint.MaxValue ||
            arena[scheduler + WarpPortableSchedulerLayout.DispatchGeneration] != previousDispatch + 1 ||
            arena[scheduler + WarpPortableSchedulerLayout.ContextState] != WarpPortableSchedulerLayout.Active) { return WarpPortableExceptionLayout.InvalidTicket; }
        for (uint worker = 0; worker < arena[descriptor + WarpPortableExceptionLayout.WorkerCount]; worker++)
        {
            uint scheduled = scheduler + arena[scheduler + WarpPortableSchedulerLayout.WorkerStart] + worker * WarpPortableSchedulerLayout.WorkerWords;
            uint entry = Worker(arena, descriptor, worker);
            if (arena[scheduled + WarpPortableSchedulerLayout.WorkerState] != WarpPortableSchedulerLayout.Ready ||
                arena[entry + WarpPortableExceptionLayout.DispatchGeneration] != 0 && arena[entry + WarpPortableExceptionLayout.DispatchGeneration] != previousDispatch)
            {
                return WarpPortableExceptionLayout.InvalidTicket;
            }
        }
        for (uint worker = 0; worker < arena[descriptor + WarpPortableExceptionLayout.WorkerCount]; worker++)
        {
            ClearWorkerRecords(arena, descriptor, worker);
            uint entry = Worker(arena, descriptor, worker);
            arena[entry + WarpPortableExceptionLayout.DispatchGeneration] = previousDispatch + 1;
            arena[entry + WarpPortableExceptionLayout.OwnerContext] = 0;
            arena[entry + WarpPortableExceptionLayout.ReportsSealed] = 0;
        }
        return 0;
    }

    public static uint DisposeRecords(uint[] arena, uint controller)
    {
        uint descriptor = Descriptor(arena);
        if (descriptor == 0) { return WarpPortableExceptionLayout.InvalidDescriptor; }
        uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
        if (ValidLifetimeController(arena, scheduler, controller) == 0 ||
            arena[scheduler + WarpPortableSchedulerLayout.ContextState] != WarpPortableSchedulerLayout.DisposedContext) { return WarpPortableExceptionLayout.InvalidTicket; }
        for (uint worker = 0; worker < arena[descriptor + WarpPortableExceptionLayout.WorkerCount]; worker++)
        {
            ClearWorkerRecords(arena, descriptor, worker);
            arena[Worker(arena, descriptor, worker) + WarpPortableExceptionLayout.Disposed] = 1;
        }
        return 0;
    }

    private static uint ValidLifetimeController(uint[] arena, uint scheduler, uint controller) =>
        scheduler >= WarpPortableHeapLayout.HeaderWords && Range(arena, scheduler, WarpPortableSchedulerLayout.HeaderWords) != 0 &&
        arena[scheduler] == WarpPortableSchedulerLayout.Magic && arena[scheduler + 1] == WarpPortableSchedulerLayout.Version &&
        arena[scheduler + WarpPortableSchedulerLayout.ArenaWords] == (uint)arena.Length && controller != 0 &&
        arena[scheduler + WarpPortableSchedulerLayout.ControllerOwner] == controller &&
        arena[scheduler + WarpPortableSchedulerLayout.RunningCount] == 0 &&
        arena[scheduler + WarpPortableSchedulerLayout.ServiceOwner] == WarpPortableSchedulerLayout.NoWorker &&
        arena[WarpPortableHeapLayout.LeaseState] == 0 && arena[WarpPortableHeapLayout.PendingResult] == 0 &&
        arena[scheduler + WarpPortableSchedulerLayout.WorkerCount] == arena[WarpPortableHeapLayout.WorkerCount] &&
        arena[scheduler + WarpPortableSchedulerLayout.WorkerStart] >= WarpPortableSchedulerLayout.HeaderWords &&
        arena[scheduler + WarpPortableSchedulerLayout.WorkerStart] <= (uint)arena.Length - scheduler &&
        Fits(arena[scheduler + WarpPortableSchedulerLayout.WorkerCount], WarpPortableSchedulerLayout.WorkerWords, (uint)arena.Length - scheduler -
            arena[scheduler + WarpPortableSchedulerLayout.WorkerStart]) != 0 ? 1u : 0u;

    private static uint ClearWorkerRecords(uint[] arena, uint descriptor, uint worker)
    {
        for (uint index = 1; index <= arena[descriptor + WarpPortableExceptionLayout.RecordsPerWorker]; index++)
        {
            ClearRecord(arena, descriptor, Record(arena, descriptor, worker, index));
        }
        for (uint index = 1; index <= arena[descriptor + WarpPortableExceptionLayout.ReportsPerWorker]; index++)
        {
            uint report = Report(arena, descriptor, worker, index); uint root = arena[report + WarpPortableExceptionLayout.ReportRoot];
            PublishRoot(arena, descriptor, root, 0, 0, 0); PublishRoot(arena, descriptor, root + 1, 0, 0, 0);
            for (uint word = 0; word < WarpPortableExceptionLayout.ReportWords; word++) { arena[report + word] = 0; }
            arena[report + WarpPortableExceptionLayout.ReportRoot] = root;
        }
        uint entry = Worker(arena, descriptor, worker);
        arena[entry + WarpPortableExceptionLayout.ActiveRecord] = 0; arena[entry + WarpPortableExceptionLayout.PreparedRecord] = 0;
        arena[entry + WarpPortableExceptionLayout.WorkerAction] = 0; arena[entry + WarpPortableExceptionLayout.WorkerFault] = 0;
        return 0;
    }
}
