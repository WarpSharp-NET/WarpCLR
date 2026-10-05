namespace WarpCLR.Compiler;

internal static partial class WarpPortableExceptionServices
{
    private static uint PrepareEscaped(uint[] arena, uint descriptor, uint record)
    {
        if (arena[record + WarpPortableExceptionLayout.Phase] != WarpPortableExceptionLayout.Terminal ||
            arena[record + WarpPortableExceptionLayout.Uncatchable] != 0 ||
            arena[record + WarpPortableExceptionLayout.BoundaryRecord] != 0 ||
            arena[record + WarpPortableExceptionLayout.TerminalCommitted] != 0 ||
            ValidateEscapedReference(arena, descriptor, record) == 0) { return WarpPortableExceptionLayout.InvalidReference; }
        uint fault = ProjectManagedTrace(arena, descriptor, record, 0, 0);
        if (fault != 0) { return fault; }
        arena[record + WarpPortableExceptionLayout.TransferReady] = 2;
        return 0;
    }

    // Header shape never grants authority. Both immutable type closure and the
    // precise runtime-owned record roots must name the same live generation.
    private static uint ValidateEscapedReference(uint[] arena, uint descriptor, uint record)
    {
        uint owner = record + WarpPortableExceptionLayout.ExceptionReference;
        uint trace = record + WarpPortableExceptionLayout.TraceReference;
        uint root = arena[record + WarpPortableExceptionLayout.RecordRoot];
        return RequireException(arena, descriptor, arena[owner], arena[owner + 1], arena[owner + 2],
                arena[record + WarpPortableExceptionLayout.ExceptionExactType]) != 0 &&
            TracePayload(arena, descriptor, record) != 0 &&
            ReferenceRootMatches(arena, descriptor, root, owner) != 0 &&
            ReferenceRootMatches(arena, descriptor, root + 1, trace) != 0 ? 1u : 0u;
    }

    private static uint ReferenceRootMatches(uint[] arena, uint descriptor, uint root, uint owner)
    {
        if (OwnedRoot(arena, descriptor, root) == 0 || Range(arena, owner, 3) == 0) { return 0; }
        uint reference = Root(arena, root) + WarpPortableHeapLayout.RootReference;
        return arena[reference] == arena[owner] && arena[reference + 1] == arena[owner + 1] &&
            arena[reference + 2] == arena[owner + 2] ? 1u : 0u;
    }

    // This runs as a separately generated scheduler service after the original
    // worker has stopped at the dedicated atomic terminal. The committed record
    // is the admitted continuation receipt; caller-supplied reference bits are
    // not accepted. Reporting never executes another source quantum.
    public static uint PublishEscapedFault(uint[] arena, uint controller, uint worker, uint run, uint dispatch, uint raise)
    {
        uint fault = BeginPublication(arena, controller, worker, run, dispatch);
        if (fault != 0) { return fault; }
        uint descriptor = arena[WarpPortableExceptionLayout.Descriptor]; uint index = Active(arena, descriptor, worker);
        if (index == 0) { return WarpPortableExceptionLayout.InvalidPhase; }
        uint record = Record(arena, descriptor, worker, index);
        if (raise == 0 || arena[record + WarpPortableExceptionLayout.RaiseGeneration] != raise ||
            arena[record + WarpPortableExceptionLayout.TerminalCommitted] != 1 ||
            arena[record + WarpPortableExceptionLayout.FaultPublished] != 0 ||
            arena[record + WarpPortableExceptionLayout.Action] != WarpPortableExceptionLayout.Escaped ||
            ValidateEscapedReference(arena, descriptor, record) == 0) { return WarpPortableExceptionLayout.InvalidReference; }
        uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
        uint scheduled = scheduler + arena[scheduler + WarpPortableSchedulerLayout.WorkerStart] + worker * WarpPortableSchedulerLayout.WorkerWords;
        uint root = arena[scheduled + WarpPortableSchedulerLayout.FrameRootStart] + arena[scheduled + WarpPortableSchedulerLayout.FrameRootCapacity];
        if (root == 0 || root > arena[WarpPortableHeapLayout.RootCount] ||
            arena[Root(arena, root) + WarpPortableHeapLayout.RootOwnership] != WarpPortableHeapLayout.RuntimeOwnedRoot ||
            arena[Root(arena, root) + WarpPortableHeapLayout.RootState] != WarpPortableHeapLayout.Allocated) { return WarpPortableExceptionLayout.InvalidOwnership; }
        arena[record + WarpPortableExceptionLayout.FaultPublished] = 1;
        return WarpPortableSchedulerServices.RecordEscapedFault(arena, scheduler, controller, worker, run,
            arena[record + WarpPortableExceptionLayout.ExceptionExactType], arena[record + WarpPortableExceptionLayout.OriginalFunction],
            arena[record + WarpPortableExceptionLayout.OriginalOffset], arena[record + WarpPortableExceptionLayout.OriginalOpCode],
            arena[record + WarpPortableExceptionLayout.LogicalTraceCount], arena[record + WarpPortableExceptionLayout.ExceptionReference],
            arena[record + WarpPortableExceptionLayout.ExceptionReference + 1], arena[record + WarpPortableExceptionLayout.ExceptionReference + 2]);
    }

    private static uint BeginPublication(uint[] arena, uint controller, uint worker, uint run, uint dispatch)
    {
        uint descriptor = Descriptor(arena);
        if (descriptor == 0) { return WarpPortableExceptionLayout.InvalidDescriptor; }
        uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
        if (scheduler < WarpPortableHeapLayout.HeaderWords || Range(arena, scheduler, WarpPortableSchedulerLayout.HeaderWords) == 0 ||
            arena[scheduler] != WarpPortableSchedulerLayout.Magic || arena[scheduler + 1] != WarpPortableSchedulerLayout.Version ||
            arena[scheduler + WarpPortableSchedulerLayout.ArenaWords] != (uint)arena.Length || controller == 0 ||
            arena[scheduler + WarpPortableSchedulerLayout.ControllerOwner] != controller || run == 0 || dispatch == 0 ||
            arena[scheduler + WarpPortableSchedulerLayout.DispatchGeneration] != dispatch ||
            worker >= arena[descriptor + WarpPortableExceptionLayout.WorkerCount] ||
            worker >= arena[scheduler + WarpPortableSchedulerLayout.WorkerCount] ||
            arena[scheduler + WarpPortableSchedulerLayout.ContextState] != WarpPortableSchedulerLayout.Active &&
                arena[scheduler + WarpPortableSchedulerLayout.ContextState] != WarpPortableSchedulerLayout.Quarantined) { return WarpPortableExceptionLayout.InvalidTicket; }
        uint start = arena[scheduler + WarpPortableSchedulerLayout.WorkerStart];
        if (start < WarpPortableSchedulerLayout.HeaderWords || start > (uint)arena.Length - scheduler ||
            Fits(worker + 1, WarpPortableSchedulerLayout.WorkerWords, (uint)arena.Length - scheduler - start) == 0) { return WarpPortableExceptionLayout.InvalidDescriptor; }
        uint scheduled = scheduler + start + worker * WarpPortableSchedulerLayout.WorkerWords;
        uint entry = Worker(arena, descriptor, worker);
        return arena[scheduled + WarpPortableSchedulerLayout.WorkerState] == WarpPortableSchedulerLayout.Running &&
            arena[scheduled + WarpPortableSchedulerLayout.RunGeneration] == run &&
            arena[entry + WarpPortableExceptionLayout.RunGeneration] == run &&
            arena[entry + WarpPortableExceptionLayout.DispatchGeneration] == dispatch &&
            SameAdmission(arena, entry, run, dispatch) != 0 &&
            arena[WarpPortableHeapLayout.LeaseState] == 1 && arena[WarpPortableHeapLayout.LeaseOwner] == worker &&
            arena[WarpPortableHeapLayout.CollectionState] != WarpPortableHeapLayout.Collecting ? 0 : WarpPortableExceptionLayout.InvalidTicket;
    }
}
