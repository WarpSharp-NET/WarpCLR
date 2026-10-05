using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableExceptionServices
{
    // Imported into the original source graph: state is its own executing bank.
    public static uint CaptureFrames(uint[] state, uint[] arena, uint controller, uint worker, uint run, uint dispatch)
    {
        uint fault = Begin(arena, controller, worker, run, dispatch);
        if (fault != 0) { return fault; }
        uint descriptor = arena[WarpPortableExceptionLayout.Descriptor]; uint entry = Worker(arena, descriptor, worker);
        if (SameAdmission(arena, entry, run, dispatch) == 0) { return WarpPortableExceptionLayout.InvalidTicket; }
        uint count = ValidateCapture(state, arena, descriptor, entry);
        if (count == 0) { return WarpPortableExceptionLayout.InvalidContinuation; }
        if (arena[entry + WarpPortableExceptionLayout.PreparedRecord] != 0) { return WarpPortableExceptionLayout.InvalidPhase; }
        fault = PruneCaught(state, arena, descriptor, worker);
        if (fault != 0) { return fault; }
        uint available = 1;
        while (available <= arena[descriptor + WarpPortableExceptionLayout.RecordsPerWorker] &&
            arena[Record(arena, descriptor, worker, available) + WarpPortableExceptionLayout.Phase] != WarpPortableExceptionLayout.Free) { available++; }
        if (available > arena[descriptor + WarpPortableExceptionLayout.RecordsPerWorker]) { return WarpPortableExceptionLayout.RecordCapacity; }
        uint record = Record(arena, descriptor, worker, available);
        if (ClearRecord(arena, descriptor, record) != 0) { return WarpPortableExceptionLayout.InvalidOwnership; }
        CopyFrames(state, arena, descriptor, worker, available);
        arena[record + WarpPortableExceptionLayout.FrameCount] = count;
        arena[record + WarpPortableExceptionLayout.Phase] = WarpPortableExceptionLayout.Prepared;
        arena[record + WarpPortableExceptionLayout.ParentRecord] = arena[entry + WarpPortableExceptionLayout.ActiveRecord];
        arena[entry + WarpPortableExceptionLayout.PreparedRecord] = available;
        arena[entry + WarpPortableExceptionLayout.OwnerContext] = state[WarpLogicalMachineLayout.OwnerContextOffset];
        arena[entry + WarpPortableExceptionLayout.RunGeneration] = run;
        arena[entry + WarpPortableExceptionLayout.DispatchGeneration] = dispatch;
        arena[entry + WarpPortableExceptionLayout.CollectionEpoch] = arena[arena[WarpPortableSchedulerLayout.HeapDescriptor] + WarpPortableSchedulerLayout.GCEpoch];
        return 0;
    }

    // This does not admit a successor ticket. An external privately authenticated
    // transition must bind the full predecessor/successor census before changed
    // run, dispatch or collection generations can resume an owned EH operation.
    public static uint RefreshContinuation(uint[] state, uint[] arena, uint controller, uint worker, uint run, uint dispatch)
    {
        uint fault = Begin(arena, controller, worker, run, dispatch);
        if (fault != 0) { return fault; }
        uint descriptor = arena[WarpPortableExceptionLayout.Descriptor]; uint entry = Worker(arena, descriptor, worker);
        if (arena[entry + WarpPortableExceptionLayout.OwnerContext] == 0 || SameAdmission(arena, entry, run, dispatch) == 0)
        { return WarpPortableExceptionLayout.InvalidTicket; }
        if (ValidateCapture(state, arena, descriptor, entry) == 0) { return WarpPortableExceptionLayout.InvalidContinuation; }
        if (arena[entry + WarpPortableExceptionLayout.PreparedRecord] != 0) { return WarpPortableExceptionLayout.InvalidPhase; }
        return PruneCaught(state, arena, descriptor, worker);
    }

    private static uint SameAdmission(uint[] arena, uint entry, uint run, uint dispatch)
    {
        if (arena[entry + WarpPortableExceptionLayout.OwnerContext] == 0) { return 1; }
        uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
        return arena[entry + WarpPortableExceptionLayout.RunGeneration] == run &&
            arena[entry + WarpPortableExceptionLayout.DispatchGeneration] == dispatch &&
            arena[entry + WarpPortableExceptionLayout.CollectionEpoch] == arena[scheduler + WarpPortableSchedulerLayout.GCEpoch] ? 1u : 0u;
    }

    private static uint ValidateCapture(uint[] state, uint[] arena, uint descriptor, uint worker)
    {
        uint stride = arena[descriptor + WarpPortableExceptionLayout.StateStride];
        if ((uint)state.Length < WarpLogicalMachineLayout.HeaderWords || stride < WarpLogicalMachineLayout.FrameHeaderWords ||
            state[WarpLogicalMachineLayout.StatusOffset] != WarpLogicalMachineLayout.Runnable ||
            state[WarpLogicalMachineLayout.FrameStrideOffset] != stride ||
            state[WarpLogicalMachineLayout.PrivateBaseOffset] != arena[descriptor + WarpPortableExceptionLayout.PrivateOffset] ||
            state[WarpLogicalMachineLayout.OwnerContextOffset] == 0 ||
            arena[worker + WarpPortableExceptionLayout.OwnerContext] != 0 && arena[worker + WarpPortableExceptionLayout.OwnerContext] != state[WarpLogicalMachineLayout.OwnerContextOffset] ||
            state[WarpLogicalMachineLayout.DepthOffset] == 0 ||
            Fits(state[WarpLogicalMachineLayout.DepthOffset], stride, (uint)state.Length - WarpLogicalMachineLayout.HeaderWords) == 0) { return 0; }
        uint count = 0;
        for (uint physical = 0; physical < state[WarpLogicalMachineLayout.DepthOffset]; physical++)
        {
            uint frame = WarpLogicalMachineLayout.HeaderWords + physical * stride;
            uint body = LookupBody(arena, descriptor, state[frame + WarpLogicalMachineLayout.FrameFunctionOffset]);
            if (body == 0 || state[frame + WarpLogicalMachineLayout.FrameActivationOffset] == 0 ||
                state[frame + WarpLogicalMachineLayout.FramePrivateWordsOffset] != arena[body + WarpPortableExceptionLayout.BodyPrivateWords] ||
                ValidMachinePc(arena, descriptor, state[frame], state[frame + 1]) == 0) { return 0; }
            if (arena[body + WarpPortableExceptionLayout.BodyMethod] == uint.MaxValue) { continue; }
            if (LookupPc(arena, descriptor, state[frame], state[frame + 1]) == 0) { return 0; }
            if (ValidateCapturedAlias(state, arena, descriptor, physical, frame, body) == 0) { return 0; }
            count++;
            if (count > arena[descriptor + WarpPortableExceptionLayout.MaximumFrames]) { return 0; }
        }
        return count;
    }

    private static uint ValidMachinePc(uint[] arena, uint descriptor, uint function, uint pc)
    {
        for (uint index = 0; index < arena[descriptor + WarpPortableExceptionLayout.PcCount]; index++)
        {
            uint row = descriptor + arena[descriptor + WarpPortableExceptionLayout.PcStart] + index * WarpPortableExceptionLayout.PcWords;
            if (arena[row + WarpPortableExceptionLayout.PcValue] == pc && arena[row + WarpPortableExceptionLayout.PcFunction] == function) { return 1; }
        }
        return 0;
    }

    private static uint CopyFrames(uint[] state, uint[] arena, uint descriptor, uint worker, uint record)
    {
        uint count = 0; uint logical = 0; uint stride = arena[descriptor + WarpPortableExceptionLayout.StateStride];
        for (uint physical = 0; physical < state[WarpLogicalMachineLayout.DepthOffset]; physical++)
        {
            uint frame = WarpLogicalMachineLayout.HeaderWords + physical * stride;
            uint site = LookupPc(arena, descriptor, state[frame], state[frame + 1]);
            if (site == 0) { continue; }
            uint body = LookupBody(arena, descriptor, state[frame]);
            logical += arena[body + WarpPortableExceptionLayout.BodyCountsDepth];
            uint captured = Frame(arena, descriptor, worker, record, ++count);
            arena[captured + WarpPortableExceptionLayout.FramePhysical] = physical + 1;
            arena[captured + WarpPortableExceptionLayout.FrameActivation] = state[frame + WarpLogicalMachineLayout.FrameActivationOffset];
            arena[captured + WarpPortableExceptionLayout.FrameFunction] = state[frame];
            arena[captured + WarpPortableExceptionLayout.FrameSite] = site;
            arena[captured + WarpPortableExceptionLayout.FramePc] = state[frame + 1];
            arena[captured + WarpPortableExceptionLayout.FramePrivateWords] = state[frame + WarpLogicalMachineLayout.FramePrivateWordsOffset];
            arena[captured + WarpPortableExceptionLayout.FrameLogicalDepth] = logical;
            arena[captured + WarpPortableExceptionLayout.FrameAliasOwnerPhysical] = state[frame + WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset];
        }
        return 0;
    }

    private static uint PruneCaught(uint[] state, uint[] arena, uint descriptor, uint worker)
    {
        uint stride = arena[descriptor + WarpPortableExceptionLayout.StateStride];
        for (uint index = 1; index <= arena[descriptor + WarpPortableExceptionLayout.RecordsPerWorker]; index++)
        {
            uint record = Record(arena, descriptor, worker, index);
            if (arena[record + WarpPortableExceptionLayout.Phase] != WarpPortableExceptionLayout.Caught) { continue; }
            uint physical = arena[record + WarpPortableExceptionLayout.CaughtFrame];
            uint clauseId = arena[record + WarpPortableExceptionLayout.CaughtClause];
            if (clauseId == 0 || clauseId > arena[descriptor + WarpPortableExceptionLayout.ClauseCount]) { return WarpPortableExceptionLayout.InvalidDescriptor; }
            uint clause = Clause(arena, descriptor, clauseId); uint live = 0;
            if (physical != 0 && physical <= state[WarpLogicalMachineLayout.DepthOffset])
            {
                uint frame = WarpLogicalMachineLayout.HeaderWords + (physical - 1) * stride;
                uint siteId = LookupPc(arena, descriptor, state[frame], state[frame + 1]);
                if (siteId != 0 && state[frame + WarpLogicalMachineLayout.FrameActivationOffset] == arena[record + WarpPortableExceptionLayout.CaughtActivation])
                {
                    uint offset = arena[Site(arena, descriptor, siteId) + WarpPortableExceptionLayout.SiteOffset];
                    live = state[frame] == arena[clause + WarpPortableExceptionLayout.ClauseFunction] &&
                        offset >= arena[clause + WarpPortableExceptionLayout.HandlerStart] && offset < arena[clause + WarpPortableExceptionLayout.HandlerEnd] ? 1u : 0u;
                }
            }
            if (live == 0 && ClearRecord(arena, descriptor, record) != 0) { return WarpPortableExceptionLayout.InvalidOwnership; }
        }
        return 0;
    }
}
