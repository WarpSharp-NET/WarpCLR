namespace WarpCLR.Compiler;

internal static partial class WarpPortableExceptionServices
{
    public static uint AdvanceUnwind(uint[] arena, uint controller, uint worker, uint run, uint dispatch, uint raise)
    {
        uint fault = Begin(arena, controller, worker, run, dispatch);
        if (fault != 0) { return fault; }
        uint descriptor = arena[WarpPortableExceptionLayout.Descriptor]; uint index = Active(arena, descriptor, worker);
        if (index == 0) { return WarpPortableExceptionLayout.InvalidPhase; }
        uint record = Record(arena, descriptor, worker, index); uint entry = Worker(arena, descriptor, worker);
        uint leaving = arena[record + WarpPortableExceptionLayout.Flags] & 1;
        if (raise == 0 || arena[record + WarpPortableExceptionLayout.RaiseGeneration] != raise ||
            arena[record + WarpPortableExceptionLayout.Phase] != WarpPortableExceptionLayout.Unwinding &&
            arena[record + WarpPortableExceptionLayout.Phase] != WarpPortableExceptionLayout.Leaving) { return WarpPortableExceptionLayout.InvalidPhase; }
        while (arena[record + WarpPortableExceptionLayout.UnwindFrame] != 0)
        {
            uint frame = arena[record + WarpPortableExceptionLayout.UnwindFrame];
            if (frame > arena[record + WarpPortableExceptionLayout.FrameCount]) { return WarpPortableExceptionLayout.InvalidDescriptor; }
            uint captured = Frame(arena, descriptor, worker, index, frame);
            uint step = UnwindStep(arena, descriptor, worker, record, captured, frame, leaving);
            if (step >= 256) { return step; }
            if (step == 2) { return 0; }
            if (step == 1) { continue; }
            if (arena[record + WarpPortableExceptionLayout.SelectedFrame] == frame) { return WarpPortableExceptionLayout.InvalidDescriptor; }
            if (leaving != 0)
            {
                arena[record + WarpPortableExceptionLayout.Phase] = WarpPortableExceptionLayout.Terminal;
                return SetAction(arena, entry, record, WarpPortableExceptionLayout.ResumeLeave);
            }
            if (arena[record + WarpPortableExceptionLayout.BoundaryPhysical] != 0 &&
                arena[captured + WarpPortableExceptionLayout.FramePhysical] <= arena[record + WarpPortableExceptionLayout.BoundaryPhysical]) { break; }
            arena[record + WarpPortableExceptionLayout.UnwindFrame]--; arena[record + WarpPortableExceptionLayout.UnwindCursor] = 0;
        }
        arena[record + WarpPortableExceptionLayout.Phase] = WarpPortableExceptionLayout.Terminal;
        return SetAction(arena, entry, record, arena[record + WarpPortableExceptionLayout.BoundaryRecord] != 0 ?
            WarpPortableExceptionLayout.RejectFilter : WarpPortableExceptionLayout.Escaped);
    }

    // 0 completes a frame; 1 advances a candidate; 2 prepares a source action.
    private static uint UnwindStep(uint[] arena, uint descriptor, uint worker, uint record, uint captured, uint frame, uint leaving)
    {
        uint siteId = arena[captured + WarpPortableExceptionLayout.FrameSite];
        if (siteId == 0 || siteId > arena[descriptor + WarpPortableExceptionLayout.SiteCount]) { return WarpPortableExceptionLayout.InvalidDescriptor; }
        uint site = Site(arena, descriptor, siteId);
        uint start = arena[site + (leaving == 0 ? WarpPortableExceptionLayout.TryList : WarpPortableExceptionLayout.LeaveList)];
        uint count = arena[site + (leaving == 0 ? WarpPortableExceptionLayout.TryCount : WarpPortableExceptionLayout.LeaveCount)];
        uint cursor = arena[record + WarpPortableExceptionLayout.UnwindCursor];
        if (ValidList(arena, descriptor, start, count) == 0 || cursor > count) { return WarpPortableExceptionLayout.InvalidDescriptor; }
        if (cursor == count) { return 0; }
        uint clauseId = arena[descriptor + start + cursor];
        if (clauseId == 0 || clauseId > arena[descriptor + WarpPortableExceptionLayout.ClauseCount]) { return WarpPortableExceptionLayout.InvalidDescriptor; }
        uint clause = Clause(arena, descriptor, clauseId);
        if (AtSelectedGroup(arena, descriptor, record, frame, clause) != 0)
        {
            arena[record + WarpPortableExceptionLayout.Phase] = WarpPortableExceptionLayout.CatchPending;
            SetAction(arena, Worker(arena, descriptor, worker), record, WarpPortableExceptionLayout.EnterCatch); return 2;
        }
        arena[record + WarpPortableExceptionLayout.UnwindCursor]++;
        uint kind = arena[clause + WarpPortableExceptionLayout.ClauseKind];
        if ((kind == 2 || kind == 4 && leaving == 0) && EligibleClause(arena, descriptor, worker, record, captured, clause) != 0)
        {
            arena[record + WarpPortableExceptionLayout.CleanupClause] = clauseId;
            arena[record + WarpPortableExceptionLayout.Phase] = WarpPortableExceptionLayout.Cleaning;
            SetAction(arena, Worker(arena, descriptor, worker), record, kind == 2 ? WarpPortableExceptionLayout.RunFinally : WarpPortableExceptionLayout.RunFault); return 2;
        }
        return 1;
    }

    private static uint AtSelectedGroup(uint[] arena, uint descriptor, uint record, uint frame, uint clause)
    {
        uint selected = arena[record + WarpPortableExceptionLayout.SelectedClause];
        return arena[record + WarpPortableExceptionLayout.SelectedFrame] == frame && selected != 0 &&
            selected <= arena[descriptor + WarpPortableExceptionLayout.ClauseCount] &&
            arena[clause + WarpPortableExceptionLayout.ClauseGroup] == arena[Clause(arena, descriptor, selected) + WarpPortableExceptionLayout.ClauseGroup] ? 1u : 0u;
    }

    public static uint EndCleanup(uint[] arena, uint controller, uint worker, uint run, uint dispatch, uint raise, uint clause)
    {
        uint fault = Begin(arena, controller, worker, run, dispatch);
        if (fault != 0) { return fault; }
        uint descriptor = arena[WarpPortableExceptionLayout.Descriptor]; uint index = Active(arena, descriptor, worker);
        if (index == 0) { return WarpPortableExceptionLayout.InvalidPhase; }
        uint record = Record(arena, descriptor, worker, index); uint entry = Worker(arena, descriptor, worker);
        if (raise == 0 || arena[record + WarpPortableExceptionLayout.RaiseGeneration] != raise ||
            arena[record + WarpPortableExceptionLayout.Phase] != WarpPortableExceptionLayout.Cleaning ||
            (arena[record + WarpPortableExceptionLayout.Flags] & 2) == 0 ||
            clause == 0 || clause != arena[record + WarpPortableExceptionLayout.CleanupClause]) { return WarpPortableExceptionLayout.InvalidPhase; }
        arena[record + WarpPortableExceptionLayout.CleanupClause] = 0;
        arena[record + WarpPortableExceptionLayout.Flags] &= 1;
        arena[record + WarpPortableExceptionLayout.Phase] = (arena[record + WarpPortableExceptionLayout.Flags] & 1) == 0 ?
            WarpPortableExceptionLayout.Unwinding : WarpPortableExceptionLayout.Leaving;
        return SetAction(arena, entry, record, WarpPortableExceptionLayout.Continue);
    }

    public static uint EndCleanupAt(uint[] arena, uint controller, uint worker, uint run, uint dispatch, uint raise, uint clause,
        uint sourceFunction, uint sourceOffset, uint sourceOpcode)
    {
        uint fault = Begin(arena, controller, worker, run, dispatch);
        if (fault != 0) { return fault; }
        uint descriptor = arena[WarpPortableExceptionLayout.Descriptor];
        uint site = LookupSite(arena, descriptor, sourceFunction, sourceOffset, sourceOpcode);
        if (sourceOpcode != 0xDC || site == 0 || clause == 0 || clause > arena[descriptor + WarpPortableExceptionLayout.ClauseCount])
        { return WarpPortableExceptionLayout.UnknownSourceSite; }
        uint cleanup = Clause(arena, descriptor, clause);
        if (sourceFunction != arena[cleanup + WarpPortableExceptionLayout.ClauseFunction] ||
            arena[cleanup + WarpPortableExceptionLayout.ClauseKind] != 2 && arena[cleanup + WarpPortableExceptionLayout.ClauseKind] != 4 ||
            sourceOffset < arena[cleanup + WarpPortableExceptionLayout.HandlerStart] || sourceOffset >= arena[cleanup + WarpPortableExceptionLayout.HandlerEnd])
        { return WarpPortableExceptionLayout.UnknownSourceSite; }
        return EndCleanup(arena, controller, worker, run, dispatch, raise, clause);
    }
}
