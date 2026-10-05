namespace WarpCLR.Compiler;

internal static partial class WarpPortableExceptionServices
{
    // Trace arrays are preallocated before source execution. They are single use
    // and remain precise reserved roots until dispatch reporting is released.
    public static uint BindTraceReport(uint[] arena, uint controller, uint worker, uint run, uint dispatch,
        uint report, uint context, uint slot, uint generation)
    {
        uint fault = Begin(arena, controller, worker, run, dispatch);
        if (fault != 0) { return fault; }
        uint descriptor = arena[WarpPortableExceptionLayout.Descriptor]; uint entry = Worker(arena, descriptor, worker);
        if (report == 0 || report > arena[descriptor + WarpPortableExceptionLayout.ReportsPerWorker] ||
            arena[entry + WarpPortableExceptionLayout.ReportsSealed] != 0 || arena[entry + WarpPortableExceptionLayout.OwnerContext] != 0 ||
            arena[entry + WarpPortableExceptionLayout.PreparedRecord] != 0) { return WarpPortableExceptionLayout.InvalidPhase; }
        uint trace = RequireObject(arena, context, slot, generation);
        if (trace == 0 || arena[trace + WarpPortableHeapLayout.SlotType] != arena[descriptor + WarpPortableExceptionLayout.TraceArrayType] ||
            arena[trace + WarpPortableHeapLayout.SlotKind] != WarpPortableHeapLayout.ValueArray ||
            arena[trace + WarpPortableHeapLayout.SlotLength] < WarpPortableExceptionTraceLayout.HeaderWords ||
            Fits(arena[descriptor + WarpPortableExceptionLayout.MaximumFrames], WarpPortableExceptionTraceLayout.FrameWords * 2,
                arena[trace + WarpPortableHeapLayout.SlotLength] - WarpPortableExceptionTraceLayout.HeaderWords) == 0 ||
            arena[trace + WarpPortableHeapLayout.SlotPayloadWords] < arena[trace + WarpPortableHeapLayout.SlotLength] ||
            UniqueTrace(arena, descriptor, context, slot, generation) == 0 ||
            generation <= arena[descriptor + arena[descriptor + WarpPortableExceptionLayout.TraceGenerations] + slot - 1]) { return WarpPortableExceptionLayout.InvalidReference; }
        uint row = Report(arena, descriptor, worker, report); uint root = arena[row + WarpPortableExceptionLayout.ReportRoot];
        if (arena[row + WarpPortableExceptionLayout.ReportState] != 0 || OwnedRoot(arena, descriptor, root + 1) == 0) { return WarpPortableExceptionLayout.InvalidOwnership; }
        PublishRoot(arena, descriptor, root + 1, context, slot, generation);
        arena[row + WarpPortableExceptionLayout.ReportTrace] = context;
        arena[row + WarpPortableExceptionLayout.ReportTrace + 1] = slot;
        arena[row + WarpPortableExceptionLayout.ReportTrace + 2] = generation;
        arena[row + WarpPortableExceptionLayout.ReportState] = 1;
        arena[descriptor + arena[descriptor + WarpPortableExceptionLayout.TraceGenerations] + slot - 1] = generation;
        return 0;
    }

    public static uint SealReports(uint[] arena, uint controller, uint worker, uint run, uint dispatch)
    {
        uint fault = Begin(arena, controller, worker, run, dispatch);
        if (fault != 0) { return fault; }
        uint descriptor = arena[WarpPortableExceptionLayout.Descriptor]; uint entry = Worker(arena, descriptor, worker);
        if (arena[entry + WarpPortableExceptionLayout.ReportsSealed] != 0 || arena[entry + WarpPortableExceptionLayout.OwnerContext] != 0) { return WarpPortableExceptionLayout.InvalidPhase; }
        for (uint report = 1; report <= arena[descriptor + WarpPortableExceptionLayout.ReportsPerWorker]; report++)
        {
            uint row = Report(arena, descriptor, worker, report);
            if (arena[row + WarpPortableExceptionLayout.ReportState] != 1 ||
                RequireObject(arena, arena[row + WarpPortableExceptionLayout.ReportTrace], arena[row + WarpPortableExceptionLayout.ReportTrace + 1],
                    arena[row + WarpPortableExceptionLayout.ReportTrace + 2]) == 0) { return WarpPortableExceptionLayout.InvalidReference; }
        }
        arena[entry + WarpPortableExceptionLayout.ReportsSealed] = 1;
        return 0;
    }

    private static uint UniqueTrace(uint[] arena, uint descriptor, uint context, uint slot, uint generation)
    {
        for (uint worker = 0; worker < arena[descriptor + WarpPortableExceptionLayout.WorkerCount]; worker++)
        {
            for (uint report = 1; report <= arena[descriptor + WarpPortableExceptionLayout.ReportsPerWorker]; report++)
            {
                uint row = Report(arena, descriptor, worker, report);
                if (arena[row + WarpPortableExceptionLayout.ReportState] != 0 && arena[row + WarpPortableExceptionLayout.ReportTrace] == context &&
                    arena[row + WarpPortableExceptionLayout.ReportTrace + 1] == slot && arena[row + WarpPortableExceptionLayout.ReportTrace + 2] == generation) { return 0; }
            }
        }
        return 1;
    }

    private static uint AvailableReport(uint[] arena, uint descriptor, uint worker)
    {
        for (uint report = 1; report <= arena[descriptor + WarpPortableExceptionLayout.ReportsPerWorker]; report++)
        {
            if (arena[Report(arena, descriptor, worker, report) + WarpPortableExceptionLayout.ReportState] == 1) { return report; }
        }
        return 0;
    }

    private static uint WriteTrace(uint[] arena, uint descriptor, uint worker, uint index, uint record, uint report)
    {
        uint source = Report(arena, descriptor, worker, report);
        uint trace = RequireObject(arena, arena[source + WarpPortableExceptionLayout.ReportTrace], arena[source + WarpPortableExceptionLayout.ReportTrace + 1],
            arena[source + WarpPortableExceptionLayout.ReportTrace + 2]);
        if (trace == 0 || arena[trace + WarpPortableHeapLayout.SlotLength] < WarpPortableExceptionTraceLayout.HeaderWords ||
            Fits(arena[descriptor + WarpPortableExceptionLayout.MaximumFrames], WarpPortableExceptionTraceLayout.FrameWords * 2,
                arena[trace + WarpPortableHeapLayout.SlotLength] - WarpPortableExceptionTraceLayout.HeaderWords) == 0) { return WarpPortableExceptionLayout.InvalidReference; }
        CopyOwner(arena, record + WarpPortableExceptionLayout.TraceReference, source + WarpPortableExceptionLayout.ReportTrace);
        uint payload = arena[trace + WarpPortableHeapLayout.SlotPayload];
        uint site = Site(arena, descriptor, LookupSite(arena, descriptor, arena[record + WarpPortableExceptionLayout.OriginalFunction],
            arena[record + WarpPortableExceptionLayout.OriginalOffset], arena[record + WarpPortableExceptionLayout.OriginalOpCode]));
        arena[payload + WarpPortableExceptionTraceLayout.Identity] = arena[record + WarpPortableExceptionLayout.TraceIdentity];
        arena[payload + WarpPortableExceptionTraceLayout.Count] = 0;
        arena[payload + WarpPortableExceptionTraceLayout.FormatVersion] = WarpPortableExceptionTraceLayout.Version;
        arena[payload + WarpPortableExceptionTraceLayout.Capacity] = arena[descriptor + WarpPortableExceptionLayout.MaximumFrames];
        arena[payload + WarpPortableExceptionTraceLayout.RawStart] = WarpPortableExceptionTraceLayout.HeaderWords;
        arena[payload + WarpPortableExceptionTraceLayout.ManagedStart] = WarpPortableExceptionTraceLayout.HeaderWords +
            arena[descriptor + WarpPortableExceptionLayout.MaximumFrames] * WarpPortableExceptionTraceLayout.FrameWords;
        arena[payload + WarpPortableExceptionTraceLayout.RawCount] = WriteLogicalFrames(arena, descriptor, worker, index, record, payload);
        arena[payload + WarpPortableExceptionTraceLayout.ProjectionState] = WarpPortableExceptionTraceLayout.Pending;
        arena[payload + WarpPortableExceptionTraceLayout.ThrowMethod] = arena[site + WarpPortableExceptionLayout.SiteMethod];
        arena[payload + WarpPortableExceptionTraceLayout.ThrowOffset] = arena[record + WarpPortableExceptionLayout.OriginalOffset];
        arena[record + WarpPortableExceptionLayout.LogicalTraceCount] = 0;
        arena[source + WarpPortableExceptionLayout.ReportState] = 2;
        arena[record + WarpPortableExceptionLayout.ReportIndex] = report;
        return PublishRoot(arena, descriptor, arena[record + WarpPortableExceptionLayout.RecordRoot] + 1,
            arena[record + WarpPortableExceptionLayout.TraceReference], arena[record + WarpPortableExceptionLayout.TraceReference + 1],
            arena[record + WarpPortableExceptionLayout.TraceReference + 2]);
    }
}
