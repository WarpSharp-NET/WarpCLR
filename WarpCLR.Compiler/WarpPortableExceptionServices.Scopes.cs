using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableExceptionServices
{
    // A return value remains in the exact source private evaluation bank and
    // under the operation lease while this helper releases caught contexts.
    // The ordinary generated return/caller tuple copy then runs inline.
    public static uint ReleaseCaughtForReturn(uint[] state, uint[] arena, uint controller, uint worker, uint run, uint dispatch,
        uint sourceFunction, uint sourceOffset, uint sourceOpcode)
    {
        uint fault = Begin(arena, controller, worker, run, dispatch);
        if (fault != 0) { return fault; }
        uint descriptor = arena[WarpPortableExceptionLayout.Descriptor]; uint entry = Worker(arena, descriptor, worker);
        if (sourceOpcode != 0x2A || ValidateCapture(state, arena, descriptor, entry) == 0) { return WarpPortableExceptionLayout.InvalidContinuation; }
        uint physical = ReturnFrame(state, arena, descriptor, sourceFunction, sourceOffset);
        if (physical == 0) { return WarpPortableExceptionLayout.UnknownSourceSite; }
        if (arena[entry + WarpPortableExceptionLayout.OwnerContext] == 0) { return 0; }
        fault = PruneCaught(state, arena, descriptor, worker);
        if (fault != 0) { return fault; }
        for (uint index = 1; index <= arena[descriptor + WarpPortableExceptionLayout.RecordsPerWorker]; index++)
        {
            uint record = Record(arena, descriptor, worker, index);
            if (arena[record + WarpPortableExceptionLayout.Phase] == WarpPortableExceptionLayout.Caught &&
                arena[record + WarpPortableExceptionLayout.CaughtFrame] >= physical && ClearRecord(arena, descriptor, record) != 0)
            { return WarpPortableExceptionLayout.InvalidOwnership; }
        }
        return 0;
    }

    private static uint ReturnFrame(uint[] state, uint[] arena, uint descriptor, uint function, uint offset)
    {
        uint found = 0; uint stride = arena[descriptor + WarpPortableExceptionLayout.StateStride];
        for (uint physical = 0; physical < state[WarpLogicalMachineLayout.DepthOffset]; physical++)
        {
            uint frame = WarpLogicalMachineLayout.HeaderWords + physical * stride;
            uint site = LookupPc(arena, descriptor, state[frame], state[frame + 1]);
            if (site == 0) { continue; }
            uint row = Site(arena, descriptor, site);
            found = state[frame] == function && arena[row + WarpPortableExceptionLayout.SiteOffset] == offset &&
                arena[row + WarpPortableExceptionLayout.SiteOpCode] == 0x2A ? physical + 1 : 0;
        }
        return found;
    }

    // Search/filter has finished before a normal unwind action reaches this.
    // Caught scopes outside the exact target handler or popped activation may
    // now release their roots; source state depth/PC remains unchanged here.
    private static uint PruneTransferScopes(uint[] state, uint[] arena, uint descriptor, uint worker, uint captured, uint pc)
    {
        uint fault = PruneCaught(state, arena, descriptor, worker);
        if (fault != 0) { return fault; }
        uint physical = arena[captured + WarpPortableExceptionLayout.FramePhysical];
        uint site = LookupPc(arena, descriptor, arena[captured + WarpPortableExceptionLayout.FrameFunction], pc);
        if (site == 0) { return WarpPortableExceptionLayout.UnknownSourceSite; }
        uint offset = arena[Site(arena, descriptor, site) + WarpPortableExceptionLayout.SiteOffset];
        for (uint index = 1; index <= arena[descriptor + WarpPortableExceptionLayout.RecordsPerWorker]; index++)
        {
            uint record = Record(arena, descriptor, worker, index);
            if (arena[record + WarpPortableExceptionLayout.Phase] != WarpPortableExceptionLayout.Caught) { continue; }
            uint owner = arena[record + WarpPortableExceptionLayout.CaughtFrame];
            uint clause = arena[record + WarpPortableExceptionLayout.CaughtClause];
            if (clause == 0 || clause > arena[descriptor + WarpPortableExceptionLayout.ClauseCount]) { return WarpPortableExceptionLayout.InvalidDescriptor; }
            uint handler = Clause(arena, descriptor, clause);
            if ((owner > physical || owner == physical && (offset < arena[handler + WarpPortableExceptionLayout.HandlerStart] ||
                offset >= arena[handler + WarpPortableExceptionLayout.HandlerEnd])) && ClearRecord(arena, descriptor, record) != 0)
            { return WarpPortableExceptionLayout.InvalidOwnership; }
        }
        return 0;
    }
}
