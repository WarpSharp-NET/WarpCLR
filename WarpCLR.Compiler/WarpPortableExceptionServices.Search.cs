namespace WarpCLR.Compiler;

internal static partial class WarpPortableExceptionServices
{
    public static uint AdvanceSearch(uint[] arena, uint controller, uint worker, uint run, uint dispatch, uint raise)
    {
        uint fault = Begin(arena, controller, worker, run, dispatch);
        if (fault != 0) { return fault; }
        uint descriptor = arena[WarpPortableExceptionLayout.Descriptor]; uint index = Active(arena, descriptor, worker);
        if (index == 0) { return WarpPortableExceptionLayout.InvalidPhase; }
        uint record = Record(arena, descriptor, worker, index); uint entry = Worker(arena, descriptor, worker);
        if (raise == 0 || arena[record + WarpPortableExceptionLayout.RaiseGeneration] != raise ||
            arena[record + WarpPortableExceptionLayout.Phase] != WarpPortableExceptionLayout.Searching) { return WarpPortableExceptionLayout.InvalidPhase; }
        while (arena[record + WarpPortableExceptionLayout.SearchFrame] != 0)
        {
            uint frame = arena[record + WarpPortableExceptionLayout.SearchFrame];
            if (frame > arena[record + WarpPortableExceptionLayout.FrameCount]) { return WarpPortableExceptionLayout.InvalidDescriptor; }
            uint captured = Frame(arena, descriptor, worker, index, frame);
            if (arena[record + WarpPortableExceptionLayout.BoundaryPhysical] != 0 &&
                arena[captured + WarpPortableExceptionLayout.FramePhysical] < arena[record + WarpPortableExceptionLayout.BoundaryPhysical]) { break; }
            uint step = SearchStep(arena, descriptor, worker, index, record, captured, frame);
            if (step >= 256) { return step; }
            if (step == 2) { return 0; }
        }
        arena[record + WarpPortableExceptionLayout.Phase] = WarpPortableExceptionLayout.Unwinding;
        arena[record + WarpPortableExceptionLayout.SelectedFrame] = 0; arena[record + WarpPortableExceptionLayout.SelectedClause] = 0;
        return SetAction(arena, entry, record, WarpPortableExceptionLayout.Continue);
    }

    // 1 means another bounded candidate; 2 means the next action is prepared.
    private static uint SearchStep(uint[] arena, uint descriptor, uint worker, uint index, uint record, uint captured, uint frame)
    {
        uint siteId = arena[captured + WarpPortableExceptionLayout.FrameSite];
        if (siteId == 0 || siteId > arena[descriptor + WarpPortableExceptionLayout.SiteCount]) { return WarpPortableExceptionLayout.InvalidDescriptor; }
        uint site = Site(arena, descriptor, siteId); uint start = arena[site + WarpPortableExceptionLayout.TryList];
        uint count = arena[site + WarpPortableExceptionLayout.TryCount]; uint cursor = arena[record + WarpPortableExceptionLayout.SearchCursor];
        if (ValidList(arena, descriptor, start, count) == 0 || cursor > count) { return WarpPortableExceptionLayout.InvalidDescriptor; }
        if (cursor == count)
        {
            arena[record + WarpPortableExceptionLayout.SearchFrame]--; arena[record + WarpPortableExceptionLayout.SearchCursor] = 0; return 1;
        }
        uint clauseId = arena[descriptor + start + cursor];
        if (clauseId == 0 || clauseId > arena[descriptor + WarpPortableExceptionLayout.ClauseCount]) { return WarpPortableExceptionLayout.InvalidDescriptor; }
        uint clause = Clause(arena, descriptor, clauseId);
        arena[record + WarpPortableExceptionLayout.SearchCursor]++;
        if (EligibleClause(arena, descriptor, worker, record, captured, clause) == 0) { return 1; }
        uint kind = arena[clause + WarpPortableExceptionLayout.ClauseKind];
        if (kind != 1 && (kind != 0 || Assignable(arena, arena[record + WarpPortableExceptionLayout.ExceptionExactType], arena[clause + WarpPortableExceptionLayout.CatchType]) == 0)) { return 1; }
        arena[record + WarpPortableExceptionLayout.SelectedFrame] = frame;
        arena[record + WarpPortableExceptionLayout.SelectedClause] = clauseId;
        arena[record + WarpPortableExceptionLayout.Phase] = kind == 1 ? WarpPortableExceptionLayout.Filtering : WarpPortableExceptionLayout.Unwinding;
        SetAction(arena, Worker(arena, descriptor, worker), record, kind == 1 ? WarpPortableExceptionLayout.RunFilter : WarpPortableExceptionLayout.Continue);
        return 2;
    }

    private static uint EligibleClause(uint[] arena, uint descriptor, uint worker, uint record, uint captured, uint clause)
    {
        uint site = Site(arena, descriptor, arena[captured + WarpPortableExceptionLayout.FrameSite]);
        uint offset = arena[site + WarpPortableExceptionLayout.SiteOffset];
        if (arena[clause + WarpPortableExceptionLayout.ClauseFunction] != arena[captured + WarpPortableExceptionLayout.FrameFunction] ||
            offset < arena[clause + WarpPortableExceptionLayout.TryStart] || offset >= arena[clause + WarpPortableExceptionLayout.TryEnd]) { return 0; }
        uint boundary = arena[record + WarpPortableExceptionLayout.BoundaryRecord];
        if (boundary == 0 || arena[captured + WarpPortableExceptionLayout.FramePhysical] != arena[record + WarpPortableExceptionLayout.BoundaryPhysical]) { return 1; }
        if (boundary > arena[descriptor + WarpPortableExceptionLayout.RecordsPerWorker]) { return 0; }
        uint outer = Record(arena, descriptor, worker, boundary); uint filterId = arena[outer + WarpPortableExceptionLayout.SelectedClause];
        if (filterId == 0 || filterId > arena[descriptor + WarpPortableExceptionLayout.ClauseCount]) { return 0; }
        uint filter = Clause(arena, descriptor, filterId);
        return arena[clause + WarpPortableExceptionLayout.TryStart] >= arena[filter + WarpPortableExceptionLayout.FilterStart] &&
            arena[clause + WarpPortableExceptionLayout.TryEnd] <= arena[filter + WarpPortableExceptionLayout.HandlerStart] ? 1u : 0u;
    }

    // This is the pure phase decision, not execution of a source filter. Only a
    // bound endfilter continuation may call it after the alias frame is removed.
    public static uint CompleteFilter(uint[] arena, uint controller, uint worker, uint run, uint dispatch, uint raise, uint decision)
    {
        uint fault = Begin(arena, controller, worker, run, dispatch);
        if (fault != 0) { return fault; }
        uint descriptor = arena[WarpPortableExceptionLayout.Descriptor]; uint index = Active(arena, descriptor, worker);
        if (index == 0) { return WarpPortableExceptionLayout.InvalidPhase; }
        uint record = Record(arena, descriptor, worker, index); uint entry = Worker(arena, descriptor, worker);
        if (raise == 0 || arena[record + WarpPortableExceptionLayout.RaiseGeneration] != raise ||
            arena[record + WarpPortableExceptionLayout.Phase] != WarpPortableExceptionLayout.Filtering) { return WarpPortableExceptionLayout.InvalidPhase; }
        arena[record + WarpPortableExceptionLayout.FilterPhysical] = 0; arena[record + WarpPortableExceptionLayout.FilterActivation] = 0;
        if (decision == 0)
        {
            arena[record + WarpPortableExceptionLayout.SelectedFrame] = 0; arena[record + WarpPortableExceptionLayout.SelectedClause] = 0;
            arena[record + WarpPortableExceptionLayout.Phase] = WarpPortableExceptionLayout.Searching;
        }
        else { arena[record + WarpPortableExceptionLayout.Phase] = WarpPortableExceptionLayout.Unwinding; }
        return SetAction(arena, entry, record, WarpPortableExceptionLayout.Continue);
    }
}
