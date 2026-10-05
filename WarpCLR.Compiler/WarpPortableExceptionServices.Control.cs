namespace WarpCLR.Compiler;

internal static partial class WarpPortableExceptionServices
{
    public static uint Rethrow(uint[] arena, uint controller, uint worker, uint run, uint dispatch,
        uint sourceFunction, uint sourceOffset, uint sourceOpcode)
    {
        uint fault = Begin(arena, controller, worker, run, dispatch);
        if (fault != 0) { return fault; }
        uint descriptor = arena[WarpPortableExceptionLayout.Descriptor]; uint entry = Worker(arena, descriptor, worker);
        uint index = PreparedAtSite(arena, descriptor, worker, sourceFunction, sourceOffset, sourceOpcode, 0);
        if (index == 0 || sourceOpcode != 0xFE1A) { return WarpPortableExceptionLayout.UnknownSourceSite; }
        uint caught = FindCaught(arena, descriptor, worker, index, sourceOffset);
        if (caught == 0) { return WarpPortableExceptionLayout.InvalidPhase; }
        uint record = Record(arena, descriptor, worker, index);
        if (arena[entry + WarpPortableExceptionLayout.NextRaise] == uint.MaxValue) { return WarpPortableExceptionLayout.GenerationExhausted; }
        if (RequireException(arena, descriptor, arena[caught + WarpPortableExceptionLayout.ExceptionReference],
            arena[caught + WarpPortableExceptionLayout.ExceptionReference + 1], arena[caught + WarpPortableExceptionLayout.ExceptionReference + 2],
            arena[caught + WarpPortableExceptionLayout.ExceptionExactType]) == 0 || ValidateEscapedReference(arena, descriptor, caught) == 0)
        { return WarpPortableExceptionLayout.InvalidReference; }
        fault = StartRaise(arena, descriptor, worker, index, sourceFunction, sourceOffset, sourceOpcode, 0);
        if (fault != 0) { return fault; }
        CopyOwner(arena, record + WarpPortableExceptionLayout.ExceptionReference, caught + WarpPortableExceptionLayout.ExceptionReference);
        CopyOwner(arena, record + WarpPortableExceptionLayout.TraceReference, caught + WarpPortableExceptionLayout.TraceReference);
        arena[record + WarpPortableExceptionLayout.ExceptionExactType] = arena[caught + WarpPortableExceptionLayout.ExceptionExactType];
        arena[record + WarpPortableExceptionLayout.TraceIdentity] = arena[caught + WarpPortableExceptionLayout.TraceIdentity];
        arena[record + WarpPortableExceptionLayout.LogicalTraceCount] = arena[caught + WarpPortableExceptionLayout.LogicalTraceCount];
        for (uint word = 0; word < 4; word++) { arena[record + WarpPortableExceptionLayout.OriginalFunction + word] = arena[caught + WarpPortableExceptionLayout.OriginalFunction + word]; }
        uint root = arena[record + WarpPortableExceptionLayout.RecordRoot];
        PublishRoot(arena, descriptor, root, arena[record + WarpPortableExceptionLayout.ExceptionReference], arena[record + WarpPortableExceptionLayout.ExceptionReference + 1],
            arena[record + WarpPortableExceptionLayout.ExceptionReference + 2]);
        PublishRoot(arena, descriptor, root + 1, arena[record + WarpPortableExceptionLayout.TraceReference], arena[record + WarpPortableExceptionLayout.TraceReference + 1],
            arena[record + WarpPortableExceptionLayout.TraceReference + 2]);
        RestoreExceptionTrace(arena, descriptor, record, RequireObject(arena, arena[record + WarpPortableExceptionLayout.ExceptionReference],
            arena[record + WarpPortableExceptionLayout.ExceptionReference + 1], arena[record + WarpPortableExceptionLayout.ExceptionReference + 2]));
        return SetAction(arena, entry, record, WarpPortableExceptionLayout.Continue);
    }

    private static uint FindCaught(uint[] arena, uint descriptor, uint worker, uint prepared, uint offset)
    {
        uint source = Record(arena, descriptor, worker, prepared);
        uint top = Frame(arena, descriptor, worker, prepared, arena[source + WarpPortableExceptionLayout.FrameCount]);
        uint result = 0; uint generation = 0;
        for (uint index = 1; index <= arena[descriptor + WarpPortableExceptionLayout.RecordsPerWorker]; index++)
        {
            uint record = Record(arena, descriptor, worker, index);
            if (arena[record + WarpPortableExceptionLayout.Phase] != WarpPortableExceptionLayout.Caught ||
                arena[record + WarpPortableExceptionLayout.CaughtFrame] != arena[top + WarpPortableExceptionLayout.FramePhysical] ||
                arena[record + WarpPortableExceptionLayout.CaughtActivation] != arena[top + WarpPortableExceptionLayout.FrameActivation]) { continue; }
            uint clauseId = arena[record + WarpPortableExceptionLayout.CaughtClause];
            if (clauseId == 0 || clauseId > arena[descriptor + WarpPortableExceptionLayout.ClauseCount]) { continue; }
            uint clause = Clause(arena, descriptor, clauseId);
            if (arena[clause + WarpPortableExceptionLayout.ClauseFunction] == arena[top + WarpPortableExceptionLayout.FrameFunction] &&
                offset >= arena[clause + WarpPortableExceptionLayout.HandlerStart] && offset < arena[clause + WarpPortableExceptionLayout.HandlerEnd] &&
                arena[record + WarpPortableExceptionLayout.RaiseGeneration] > generation)
            {
                result = record; generation = arena[record + WarpPortableExceptionLayout.RaiseGeneration];
            }
        }
        return result;
    }

    public static uint BeginLeave(uint[] arena, uint controller, uint worker, uint run, uint dispatch,
        uint sourceFunction, uint sourceOffset, uint sourceOpcode)
    {
        uint fault = Begin(arena, controller, worker, run, dispatch);
        if (fault != 0) { return fault; }
        uint descriptor = arena[WarpPortableExceptionLayout.Descriptor]; uint entry = Worker(arena, descriptor, worker);
        uint index = PreparedAtSite(arena, descriptor, worker, sourceFunction, sourceOffset, sourceOpcode, 0);
        if (index == 0 || sourceOpcode != 0xDD && sourceOpcode != 0xDE) { return WarpPortableExceptionLayout.UnknownSourceSite; }
        if (arena[entry + WarpPortableExceptionLayout.NextRaise] == uint.MaxValue) { return WarpPortableExceptionLayout.GenerationExhausted; }
        uint siteId = LookupSite(arena, descriptor, sourceFunction, sourceOffset, sourceOpcode); uint site = Site(arena, descriptor, siteId);
        uint target = arena[site + WarpPortableExceptionLayout.LeaveTargetPc];
        if (ValidMachinePc(arena, descriptor, sourceFunction, target) == 0) { return WarpPortableExceptionLayout.InvalidContinuation; }
        uint record = Record(arena, descriptor, worker, index);
        arena[record + WarpPortableExceptionLayout.RaiseGeneration] = ++arena[entry + WarpPortableExceptionLayout.NextRaise];
        arena[record + WarpPortableExceptionLayout.Flags] = 1;
        arena[record + WarpPortableExceptionLayout.OriginalFunction] = sourceFunction;
        arena[record + WarpPortableExceptionLayout.OriginalOffset] = sourceOffset;
        arena[record + WarpPortableExceptionLayout.OriginalOpCode] = sourceOpcode;
        arena[record + WarpPortableExceptionLayout.LeaveTarget] = target;
        arena[record + WarpPortableExceptionLayout.UnwindFrame] = arena[record + WarpPortableExceptionLayout.FrameCount];
        arena[record + WarpPortableExceptionLayout.Phase] = WarpPortableExceptionLayout.Leaving;
        arena[entry + WarpPortableExceptionLayout.PreparedRecord] = 0;
        arena[entry + WarpPortableExceptionLayout.ActiveRecord] = index;
        return SetAction(arena, entry, record, WarpPortableExceptionLayout.Continue);
    }

    public static uint RaiseResource(uint[] arena, uint controller, uint worker, uint run, uint dispatch,
        uint sourceFunction, uint sourceOffset, uint sourceOpcode, uint resourceKind,
        uint requestedLow, uint requestedHigh, uint availableLow, uint availableHigh)
    {
        uint fault = Begin(arena, controller, worker, run, dispatch);
        if (fault != 0) { return fault; }
        uint descriptor = arena[WarpPortableExceptionLayout.Descriptor]; uint entry = Worker(arena, descriptor, worker);
        uint index = arena[entry + WarpPortableExceptionLayout.PreparedRecord];
        if (index == 0 || index > arena[descriptor + WarpPortableExceptionLayout.RecordsPerWorker] || resourceKind == 0) { return WarpPortableExceptionLayout.InvalidPhase; }
        uint record = Record(arena, descriptor, worker, index);
        if (arena[record + WarpPortableExceptionLayout.FrameCount] == 0 ||
            arena[record + WarpPortableExceptionLayout.FrameCount] > arena[descriptor + WarpPortableExceptionLayout.MaximumFrames]) { return WarpPortableExceptionLayout.InvalidDescriptor; }
        uint site = LookupSite(arena, descriptor, sourceFunction, sourceOffset, sourceOpcode);
        uint top = Frame(arena, descriptor, worker, index, arena[record + WarpPortableExceptionLayout.FrameCount]);
        if (site == 0 || arena[record + WarpPortableExceptionLayout.Phase] != WarpPortableExceptionLayout.Prepared ||
            arena[top + WarpPortableExceptionLayout.FrameSite] != site) { return WarpPortableExceptionLayout.UnknownSourceSite; }
        if (arena[entry + WarpPortableExceptionLayout.NextRaise] == uint.MaxValue) { return WarpPortableExceptionLayout.GenerationExhausted; }
        arena[record + WarpPortableExceptionLayout.RaiseGeneration] = ++arena[entry + WarpPortableExceptionLayout.NextRaise];
        arena[record + WarpPortableExceptionLayout.OriginalFunction] = sourceFunction;
        arena[record + WarpPortableExceptionLayout.OriginalOffset] = sourceOffset;
        arena[record + WarpPortableExceptionLayout.OriginalOpCode] = sourceOpcode;
        arena[record + WarpPortableExceptionLayout.RuntimeKind] = resourceKind;
        arena[record + WarpPortableExceptionLayout.RequestedLow] = requestedLow;
        arena[record + WarpPortableExceptionLayout.RequestedHigh] = requestedHigh;
        arena[record + WarpPortableExceptionLayout.AvailableLow] = availableLow;
        arena[record + WarpPortableExceptionLayout.AvailableHigh] = availableHigh;
        arena[record + WarpPortableExceptionLayout.Uncatchable] = 1;
        arena[record + WarpPortableExceptionLayout.ExceptionExactType] = resourceKind == 2 ? arena[descriptor + WarpPortableExceptionLayout.StackOverflowType] : 0;
        arena[record + WarpPortableExceptionLayout.Phase] = WarpPortableExceptionLayout.Terminal;
        arena[entry + WarpPortableExceptionLayout.PreparedRecord] = 0; arena[entry + WarpPortableExceptionLayout.ActiveRecord] = index;
        return SetAction(arena, entry, record, WarpPortableExceptionLayout.ResourceTermination);
    }
}
