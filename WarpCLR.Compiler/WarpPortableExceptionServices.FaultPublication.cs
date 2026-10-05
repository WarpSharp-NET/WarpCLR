namespace WarpCLR.Compiler;

internal static partial class WarpPortableExceptionServices
{
    internal static uint ValidateFaultOperationTable(uint[] arena, uint function, uint offset, uint opcode, uint effect, uint type, uint faultDescriptor) =>
        FaultOperationExists(arena, arena[WarpPortableExceptionLayout.Descriptor], function, offset, opcode, effect, type, faultDescriptor) != 0 ? 0 : WarpPortableExceptionLayout.UnknownSourceSite;

    internal static uint ValidateFaultPublication(uint[] arena, uint descriptor, uint worker, uint index, uint raise,
        uint function, uint offset, uint opcode, uint effect, uint context, uint slot, uint generation, uint type, uint faultDescriptor)
    {
        uint record = Record(arena, descriptor, worker, index), entry = Worker(arena, descriptor, worker);
        if (arena[entry + WarpPortableExceptionLayout.ActiveRecord] != index || arena[entry + WarpPortableExceptionLayout.PreparedRecord] != 0 ||
            arena[record + WarpPortableExceptionLayout.Phase] != WarpPortableExceptionLayout.Searching ||
            arena[record + WarpPortableExceptionLayout.RaiseGeneration] != raise || arena[record + WarpPortableExceptionLayout.OriginalFunction] != function ||
            arena[record + WarpPortableExceptionLayout.OriginalOffset] != offset || arena[record + WarpPortableExceptionLayout.OriginalOpCode] != opcode ||
            arena[record + WarpPortableExceptionLayout.OriginalEffect] != effect || arena[record + WarpPortableExceptionLayout.ExceptionExactType] != type ||
            arena[record + WarpPortableExceptionLayout.Action] != WarpPortableExceptionLayout.Continue ||
            arena[record + WarpPortableExceptionLayout.SearchFrame] != arena[record + WarpPortableExceptionLayout.FrameCount] ||
            arena[record + WarpPortableExceptionLayout.SearchCursor] != 0 || arena[record + WarpPortableExceptionLayout.SelectedClause] != 0 ||
            FaultOperationExists(arena, descriptor, function, offset, opcode, effect, type, faultDescriptor) == 0)
        { return WarpPortableExceptionLayout.InvalidPhase; }
        uint owner = record + WarpPortableExceptionLayout.ExceptionReference;
        uint exception = RequireException(arena, descriptor, context, slot, generation, type);
        if (exception == 0 || arena[owner] != context || arena[owner + 1] != slot || arena[owner + 2] != generation ||
            FaultRecordRootsMatch(arena, descriptor, record) == 0) { return WarpPortableExceptionLayout.InvalidOwnership; }
        uint report = arena[record + WarpPortableExceptionLayout.ReportIndex];
        if (report == 0 || report > arena[descriptor + WarpPortableExceptionLayout.ReportsPerWorker]) { return WarpPortableExceptionLayout.InvalidReference; }
        uint row = Report(arena, descriptor, worker, report), reference = row + WarpPortableExceptionLayout.ReportTrace;
        uint trace = record + WarpPortableExceptionLayout.TraceReference;
        if (arena[row + WarpPortableExceptionLayout.ReportState] != 2 || arena[reference] != arena[trace] ||
            arena[reference + 1] != arena[trace + 1] || arena[reference + 2] != arena[trace + 2] ||
            FaultRootMatches(arena, arena[row + WarpPortableExceptionLayout.ReportRoot] + 1, reference) == 0)
        { return WarpPortableExceptionLayout.InvalidOwnership; }
        uint payload = arena[exception + WarpPortableHeapLayout.SlotPayload];
        uint site = Site(arena, descriptor, LookupSite(arena, descriptor, function, offset, opcode));
        if (arena[payload + WarpPortableSourceExceptionLayout.TraceReferenceWord] != arena[trace] ||
            arena[payload + WarpPortableSourceExceptionLayout.TraceReferenceWord + 1] != arena[trace + 1] ||
            arena[payload + WarpPortableSourceExceptionLayout.TraceReferenceWord + 2] != arena[trace + 2] ||
            arena[payload + WarpPortableSourceExceptionLayout.TraceIdentityWord] != arena[record + WarpPortableExceptionLayout.TraceIdentity] ||
            arena[payload + WarpPortableSourceExceptionLayout.TraceIdentityWord] == 0 ||
            arena[payload + WarpPortableSourceExceptionLayout.ThrowMethodWord] != arena[site + WarpPortableExceptionLayout.SiteMethod] ||
            arena[payload + WarpPortableSourceExceptionLayout.ThrowOffsetWord] != offset)
        { return WarpPortableExceptionLayout.InvalidReference; }
        payload = TracePayload(arena, descriptor, record);
        if (payload == 0 || arena[payload + WarpPortableExceptionTraceLayout.ProjectionState] != WarpPortableExceptionTraceLayout.Pending ||
            arena[payload + WarpPortableExceptionTraceLayout.Count] != 0 ||
            arena[payload + WarpPortableExceptionTraceLayout.ThrowMethod] != arena[site + WarpPortableExceptionLayout.SiteMethod] ||
            arena[payload + WarpPortableExceptionTraceLayout.ThrowOffset] != offset) { return WarpPortableExceptionLayout.InvalidReference; }
        return ValidateFaultTraceFrames(arena, descriptor, worker, index, record, payload);
    }

    private static uint ValidateFaultTraceFrames(uint[] arena, uint descriptor, uint worker, uint index, uint record, uint payload)
    {
        uint count = 0;
        for (uint frame = 1; frame <= arena[record + WarpPortableExceptionLayout.FrameCount]; frame++)
        {
            uint captured = Frame(arena, descriptor, worker, index, frame), body = LookupBody(arena, descriptor, arena[captured + WarpPortableExceptionLayout.FrameFunction]);
            if (arena[body + WarpPortableExceptionLayout.BodyCountsDepth] == 0) { continue; }
            uint site = Site(arena, descriptor, LogicalFrameSite(arena, descriptor, worker, index, record, captured));
            uint source = payload + arena[payload + WarpPortableExceptionTraceLayout.RawStart] + count * WarpPortableExceptionTraceLayout.FrameWords;
            if (arena[source + WarpPortableExceptionTraceLayout.Method] != arena[site + WarpPortableExceptionLayout.SiteMethod] ||
                arena[source + WarpPortableExceptionTraceLayout.Offset] != arena[site + WarpPortableExceptionLayout.SiteOffset] ||
                arena[source + WarpPortableExceptionTraceLayout.Physical] != arena[captured + WarpPortableExceptionLayout.FramePhysical] ||
                arena[source + WarpPortableExceptionTraceLayout.Activation] != arena[captured + WarpPortableExceptionLayout.FrameActivation])
            { return WarpPortableExceptionLayout.InvalidReference; }
            count++;
        }
        return count != 0 && count == arena[payload + WarpPortableExceptionTraceLayout.RawCount] ? 0 : WarpPortableExceptionLayout.InvalidReference;
    }
}
