namespace WarpCLR.Compiler;

// Every mutation runs under the exact scheduler controller and worker service
// lease. A failed guard neither publishes roots nor changes source state.
internal static partial class WarpPortableExceptionServices
{
    private static uint Begin(uint[] arena, uint controller, uint worker, uint run, uint dispatch)
    {
        uint descriptor = Descriptor(arena);
        if (descriptor == 0) { return WarpPortableExceptionLayout.InvalidDescriptor; }
        if (worker >= arena[descriptor + WarpPortableExceptionLayout.WorkerCount] || run == 0 || dispatch == 0)
        {
            return WarpPortableExceptionLayout.InvalidTicket;
        }
        uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
        if (scheduler < WarpPortableHeapLayout.HeaderWords || Range(arena, scheduler, WarpPortableSchedulerLayout.HeaderWords) == 0 ||
            arena[scheduler] != WarpPortableSchedulerLayout.Magic || arena[scheduler + 1] != WarpPortableSchedulerLayout.Version ||
            arena[scheduler + WarpPortableSchedulerLayout.ArenaWords] != (uint)arena.Length || controller == 0 ||
            arena[scheduler + WarpPortableSchedulerLayout.ControllerOwner] != controller ||
            arena[scheduler + WarpPortableSchedulerLayout.ContextState] != WarpPortableSchedulerLayout.Active ||
            arena[scheduler + WarpPortableSchedulerLayout.DispatchGeneration] != dispatch ||
            worker >= arena[scheduler + WarpPortableSchedulerLayout.WorkerCount] ||
            arena[scheduler + WarpPortableSchedulerLayout.GCState] == WarpPortableSchedulerLayout.GCCollecting)
        {
            return WarpPortableExceptionLayout.InvalidTicket;
        }
        uint start = arena[scheduler + WarpPortableSchedulerLayout.WorkerStart];
        if (start < WarpPortableSchedulerLayout.HeaderWords || start > (uint)arena.Length - scheduler ||
            Fits(worker + 1, WarpPortableSchedulerLayout.WorkerWords, (uint)arena.Length - scheduler - start) == 0)
        {
            return WarpPortableExceptionLayout.InvalidDescriptor;
        }
        uint scheduled = scheduler + start + worker * WarpPortableSchedulerLayout.WorkerWords;
        if (arena[scheduled + WarpPortableSchedulerLayout.WorkerState] != WarpPortableSchedulerLayout.Running ||
            arena[scheduled + WarpPortableSchedulerLayout.RunGeneration] != run ||
            arena[WarpPortableHeapLayout.LeaseState] != 1 || arena[WarpPortableHeapLayout.LeaseOwner] != worker ||
            arena[WarpPortableHeapLayout.CollectionState] == WarpPortableHeapLayout.Collecting)
        {
            return WarpPortableExceptionLayout.InvalidTicket;
        }
        uint entry = Worker(arena, descriptor, worker);
        if (arena[entry + WarpPortableExceptionLayout.Disposed] != 0 ||
            SameAdmission(arena, entry, run, dispatch) == 0 ||
            arena[entry + WarpPortableExceptionLayout.DispatchGeneration] != 0 && arena[entry + WarpPortableExceptionLayout.DispatchGeneration] != dispatch)
        {
            return WarpPortableExceptionLayout.InvalidTicket;
        }
        return 0;
    }

    private static uint Worker(uint[] arena, uint descriptor, uint worker) =>
        descriptor + arena[descriptor + WarpPortableExceptionLayout.WorkerStart] + worker * WarpPortableExceptionLayout.WorkerWords;

    private static uint Record(uint[] arena, uint descriptor, uint worker, uint record) => descriptor +
        arena[descriptor + WarpPortableExceptionLayout.RecordStart] +
        (worker * arena[descriptor + WarpPortableExceptionLayout.RecordsPerWorker] + record - 1) * WarpPortableExceptionLayout.RecordWords;

    private static uint Frame(uint[] arena, uint descriptor, uint worker, uint record, uint frame) => descriptor +
        arena[descriptor + WarpPortableExceptionLayout.FrameStart] +
        ((worker * arena[descriptor + WarpPortableExceptionLayout.RecordsPerWorker] + record - 1) *
            arena[descriptor + WarpPortableExceptionLayout.MaximumFrames] + frame - 1) * WarpPortableExceptionLayout.FrameWords;

    private static uint Report(uint[] arena, uint descriptor, uint worker, uint report) => descriptor +
        arena[descriptor + WarpPortableExceptionLayout.ReportStart] +
        (worker * arena[descriptor + WarpPortableExceptionLayout.ReportsPerWorker] + report - 1) * WarpPortableExceptionLayout.ReportWords;

    private static uint Site(uint[] arena, uint descriptor, uint site) => descriptor +
        arena[descriptor + WarpPortableExceptionLayout.SiteStart] + (site - 1) * WarpPortableExceptionLayout.SiteWords;

    private static uint Clause(uint[] arena, uint descriptor, uint clause) => descriptor +
        arena[descriptor + WarpPortableExceptionLayout.ClauseStart] + (clause - 1) * WarpPortableExceptionLayout.ClauseWords;

    private static uint SetAction(uint[] arena, uint worker, uint record, uint action)
    {
        arena[record + WarpPortableExceptionLayout.Action] = action;
        arena[worker + WarpPortableExceptionLayout.WorkerAction] = action;
        arena[worker + WarpPortableExceptionLayout.WorkerFault] = 0;
        return 0;
    }

    private static uint Active(uint[] arena, uint descriptor, uint worker)
    {
        uint record = arena[Worker(arena, descriptor, worker) + WarpPortableExceptionLayout.ActiveRecord];
        return record != 0 && record <= arena[descriptor + WarpPortableExceptionLayout.RecordsPerWorker] ? record : 0;
    }
}
