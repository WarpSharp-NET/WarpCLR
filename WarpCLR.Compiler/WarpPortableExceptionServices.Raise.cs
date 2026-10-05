namespace WarpCLR.Compiler;

internal static partial class WarpPortableExceptionServices
{
    public static uint RaiseReference(uint[] arena, uint controller, uint worker, uint run, uint dispatch,
        uint sourceFunction, uint sourceOffset, uint sourceOpcode, uint effectIndex,
        uint context, uint slot, uint generation, uint exactType)
    {
        uint fault = Begin(arena, controller, worker, run, dispatch);
        if (fault != 0) { return fault; }
        uint descriptor = arena[WarpPortableExceptionLayout.Descriptor]; uint entry = Worker(arena, descriptor, worker);
        uint index = PreparedAtSite(arena, descriptor, worker, sourceFunction, sourceOffset, sourceOpcode, effectIndex);
        if (index == 0 || sourceOpcode != 0x7A || effectIndex != 1) { return WarpPortableExceptionLayout.UnknownSourceSite; }
        uint exception = RequireException(arena, descriptor, context, slot, generation, exactType);
        if (exception == 0) { return WarpPortableExceptionLayout.InvalidReference; }
        uint report = AvailableReport(arena, descriptor, worker);
        if (report == 0 || arena[entry + WarpPortableExceptionLayout.ReportsSealed] != 1) { return WarpPortableExceptionLayout.ReportCapacity; }
        uint traceReport = Report(arena, descriptor, worker, report);
        uint trace = RequireObject(arena, arena[traceReport + WarpPortableExceptionLayout.ReportTrace], arena[traceReport + WarpPortableExceptionLayout.ReportTrace + 1],
            arena[traceReport + WarpPortableExceptionLayout.ReportTrace + 2]);
        if (trace == 0 || arena[trace + WarpPortableHeapLayout.SlotType] != arena[descriptor + WarpPortableExceptionLayout.TraceArrayType] ||
            arena[trace + WarpPortableHeapLayout.SlotLength] < WarpPortableExceptionTraceLayout.HeaderWords ||
            Fits(arena[descriptor + WarpPortableExceptionLayout.MaximumFrames], WarpPortableExceptionTraceLayout.FrameWords * 2,
                arena[trace + WarpPortableHeapLayout.SlotLength] - WarpPortableExceptionTraceLayout.HeaderWords) == 0) { return WarpPortableExceptionLayout.InvalidReference; }
        if (arena[entry + WarpPortableExceptionLayout.NextRaise] == uint.MaxValue || arena[descriptor + WarpPortableExceptionLayout.NextTrace] == uint.MaxValue)
        {
            return WarpPortableExceptionLayout.GenerationExhausted;
        }
        uint record = Record(arena, descriptor, worker, index);
        uint root = arena[record + WarpPortableExceptionLayout.RecordRoot];
        if (OwnedRoot(arena, descriptor, root) == 0 || OwnedRoot(arena, descriptor, root + 1) == 0) { return WarpPortableExceptionLayout.InvalidOwnership; }
        fault = StartRaise(arena, descriptor, worker, index, sourceFunction, sourceOffset, sourceOpcode, effectIndex);
        if (fault != 0) { return fault; }
        return InitializeRaisedReference(arena, descriptor, worker, index, record, exception, report, context, slot, generation, exactType);
    }

    private static uint InitializeRaisedReference(uint[] arena, uint descriptor, uint worker, uint index, uint record,
        uint exception, uint report, uint context, uint slot, uint generation, uint exactType)
    {
        uint entry = Worker(arena, descriptor, worker); uint root = arena[record + WarpPortableExceptionLayout.RecordRoot];
        arena[record + WarpPortableExceptionLayout.ExceptionReference] = context;
        arena[record + WarpPortableExceptionLayout.ExceptionReference + 1] = slot;
        arena[record + WarpPortableExceptionLayout.ExceptionReference + 2] = generation;
        arena[record + WarpPortableExceptionLayout.ExceptionExactType] = exactType;
        arena[record + WarpPortableExceptionLayout.TraceIdentity] = ++arena[descriptor + WarpPortableExceptionLayout.NextTrace];
        PublishRoot(arena, descriptor, root, context, slot, generation);
        uint fault = WriteTrace(arena, descriptor, worker, index, record, report);
        if (fault != 0) { return fault; }
        RestoreExceptionTrace(arena, descriptor, record, exception);
        return SetAction(arena, entry, record, WarpPortableExceptionLayout.Continue);
    }

    private static uint RestoreExceptionTrace(uint[] arena, uint descriptor, uint record, uint exception)
    {
        uint payload = arena[exception + WarpPortableHeapLayout.SlotPayload];
        CopyOwner(arena, payload + WarpPortableSourceExceptionLayout.TraceReferenceWord, record + WarpPortableExceptionLayout.TraceReference);
        arena[payload + WarpPortableSourceExceptionLayout.TraceIdentityWord] = arena[record + WarpPortableExceptionLayout.TraceIdentity];
        uint site = Site(arena, descriptor, LookupSite(arena, descriptor, arena[record + WarpPortableExceptionLayout.OriginalFunction],
            arena[record + WarpPortableExceptionLayout.OriginalOffset], arena[record + WarpPortableExceptionLayout.OriginalOpCode]));
        arena[payload + WarpPortableSourceExceptionLayout.ThrowMethodWord] = arena[site + WarpPortableExceptionLayout.SiteMethod];
        arena[payload + WarpPortableSourceExceptionLayout.ThrowOffsetWord] = arena[record + WarpPortableExceptionLayout.OriginalOffset];
        return 0;
    }

    private static uint PreparedAtSite(uint[] arena, uint descriptor, uint worker, uint function, uint offset, uint opcode, uint effect)
    {
        uint index = arena[Worker(arena, descriptor, worker) + WarpPortableExceptionLayout.PreparedRecord];
        if (index == 0 || index > arena[descriptor + WarpPortableExceptionLayout.RecordsPerWorker]) { return 0; }
        uint record = Record(arena, descriptor, worker, index);
        if (arena[record + WarpPortableExceptionLayout.Phase] != WarpPortableExceptionLayout.Prepared || arena[record + WarpPortableExceptionLayout.FrameCount] == 0) { return 0; }
        uint site = LookupSite(arena, descriptor, function, offset, opcode);
        if (site == 0 || effect >= arena[Site(arena, descriptor, site) + WarpPortableExceptionLayout.SiteEffectCount]) { return 0; }
        uint top = Frame(arena, descriptor, worker, index, arena[record + WarpPortableExceptionLayout.FrameCount]);
        return arena[top + WarpPortableExceptionLayout.FrameSite] == site ? index : 0;
    }

    private static uint StartRaise(uint[] arena, uint descriptor, uint worker, uint index, uint function, uint offset, uint opcode, uint effect)
    {
        uint entry = Worker(arena, descriptor, worker); uint record = Record(arena, descriptor, worker, index);
        uint parent = Active(arena, descriptor, worker);
        if (parent != 0)
        {
            uint prior = Record(arena, descriptor, worker, parent);
            uint phase = arena[prior + WarpPortableExceptionLayout.Phase];
            if (phase == WarpPortableExceptionLayout.Filtering)
            {
                if (arena[prior + WarpPortableExceptionLayout.FilterPhysical] == 0) { return WarpPortableExceptionLayout.NeedsFilterAlias; }
                arena[record + WarpPortableExceptionLayout.BoundaryRecord] = parent;
                arena[record + WarpPortableExceptionLayout.BoundaryPhysical] = arena[prior + WarpPortableExceptionLayout.FilterPhysical];
            }
            else if (phase == WarpPortableExceptionLayout.Cleaning)
            {
                arena[record + WarpPortableExceptionLayout.BoundaryRecord] = arena[prior + WarpPortableExceptionLayout.BoundaryRecord];
                arena[record + WarpPortableExceptionLayout.BoundaryPhysical] = arena[prior + WarpPortableExceptionLayout.BoundaryPhysical];
                parent = arena[prior + WarpPortableExceptionLayout.ParentRecord];
                if (ClearRecord(arena, descriptor, prior) != 0) { return WarpPortableExceptionLayout.InvalidOwnership; }
            }
            else { return WarpPortableExceptionLayout.InvalidPhase; }
        }
        arena[record + WarpPortableExceptionLayout.ParentRecord] = parent;
        arena[record + WarpPortableExceptionLayout.RaiseGeneration] = ++arena[entry + WarpPortableExceptionLayout.NextRaise];
        arena[record + WarpPortableExceptionLayout.OriginalFunction] = function;
        arena[record + WarpPortableExceptionLayout.OriginalOffset] = offset;
        arena[record + WarpPortableExceptionLayout.OriginalOpCode] = opcode;
        arena[record + WarpPortableExceptionLayout.OriginalEffect] = effect;
        arena[record + WarpPortableExceptionLayout.SearchFrame] = arena[record + WarpPortableExceptionLayout.FrameCount];
        arena[record + WarpPortableExceptionLayout.UnwindFrame] = arena[record + WarpPortableExceptionLayout.FrameCount];
        arena[record + WarpPortableExceptionLayout.Phase] = WarpPortableExceptionLayout.Searching;
        arena[entry + WarpPortableExceptionLayout.ActiveRecord] = index;
        arena[entry + WarpPortableExceptionLayout.PreparedRecord] = 0;
        return 0;
    }

    // The current type schema has no proven operation-specific exception-data
    // factory. An implicit fault may not publish a default exception object.
    public static uint RaisePreparedImplicit(uint[] arena, uint controller, uint worker, uint run, uint dispatch, uint faultId)
    {
        uint fault = Begin(arena, controller, worker, run, dispatch);
        if (fault != 0) { return fault; }
        uint descriptor = arena[WarpPortableExceptionLayout.Descriptor];
        return faultId != 0 && faultId <= arena[descriptor + WarpPortableExceptionLayout.FaultCount] ?
            WarpPortableExceptionLayout.NeedsFaultFactory : WarpPortableExceptionLayout.UnknownSourceSite;
    }
}
