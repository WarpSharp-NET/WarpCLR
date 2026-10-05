namespace WarpCLR.Compiler;

internal static partial class WarpPortableExceptionServices
{
    internal static uint RaiseFaultOperation(uint[] arena, uint controller, uint worker, uint run, uint dispatch,
        uint expectedRecord, uint function, uint offset, uint opcode, uint effect, uint context, uint slot, uint generation, uint exactType, uint faultDescriptor)
    {
        uint fault = Begin(arena, controller, worker, run, dispatch);
        if (fault != 0) { return fault; }
        uint descriptor = arena[WarpPortableExceptionLayout.Descriptor], entry = Worker(arena, descriptor, worker);
        uint index = PreparedAtSite(arena, descriptor, worker, function, offset, opcode, effect);
        if (index != expectedRecord || index == 0 || FaultOperationExists(arena, descriptor, function, offset, opcode, effect, exactType, faultDescriptor) == 0)
        { return WarpPortableExceptionLayout.UnknownSourceSite; }
        uint exception = RequireException(arena, descriptor, context, slot, generation, exactType);
        if (exception == 0) { return WarpPortableExceptionLayout.InvalidReference; }
        uint report = AvailableReport(arena, descriptor, worker);
        if (report == 0 || arena[entry + WarpPortableExceptionLayout.ReportsSealed] != 1) { return WarpPortableExceptionLayout.ReportCapacity; }
        fault = ValidateFaultTraceReport(arena, descriptor, worker, report);
        if (fault != 0) { return fault; }
        if (arena[entry + WarpPortableExceptionLayout.NextRaise] == uint.MaxValue || arena[descriptor + WarpPortableExceptionLayout.NextTrace] == uint.MaxValue)
        { return WarpPortableExceptionLayout.GenerationExhausted; }
        uint record = Record(arena, descriptor, worker, index), root = arena[record + WarpPortableExceptionLayout.RecordRoot];
        if (OwnedRoot(arena, descriptor, root) == 0 || OwnedRoot(arena, descriptor, root + 1) == 0) { return WarpPortableExceptionLayout.InvalidOwnership; }
        uint parent = Active(arena, descriptor, worker);
        if (parent != 0)
        {
            uint prior = Record(arena, descriptor, worker, parent), phase = arena[prior + WarpPortableExceptionLayout.Phase];
            if (phase == WarpPortableExceptionLayout.Filtering)
            {
                if (arena[prior + WarpPortableExceptionLayout.FilterPhysical] == 0) { return WarpPortableExceptionLayout.NeedsFilterAlias; }
            }
            else if (phase != WarpPortableExceptionLayout.Cleaning) { return WarpPortableExceptionLayout.InvalidPhase; }
            else if (FaultRecordRootsMatch(arena, descriptor, prior) == 0) { return WarpPortableExceptionLayout.InvalidOwnership; }
        }
        // The retained operation lease makes the preceding validation stable
        // across helper quanta. StartRaise cannot now fail after a payload write.
        fault = StartRaise(arena, descriptor, worker, index, function, offset, opcode, effect);
        if (fault != 0) { return fault; }
        return InitializeRaisedReference(arena, descriptor, worker, index, record, exception, report, context, slot, generation, exactType);
    }

    private static uint FaultOperationExists(uint[] arena, uint descriptor, uint function, uint offset, uint opcode, uint effect, uint type, uint faultDescriptor)
    {
        uint site = LookupSite(arena, descriptor, function, offset, opcode), matches = 0;
        if (site == 0 || effect >= arena[Site(arena, descriptor, site) + WarpPortableExceptionLayout.SiteEffectCount]) { return 0; }
        for (uint index = 0; index < arena[descriptor + WarpPortableExceptionLayout.FaultCount]; index++)
        {
            uint row = descriptor + arena[descriptor + WarpPortableExceptionLayout.FaultStart] + index * WarpPortableExceptionLayout.FaultWords;
            if (arena[row + WarpPortableExceptionLayout.FaultSite] == site && arena[row + WarpPortableExceptionLayout.FaultEffect] == effect &&
                arena[row + WarpPortableExceptionLayout.FaultType] == type && arena[row + WarpPortableExceptionLayout.FaultDescriptor] == faultDescriptor &&
                arena[row + WarpPortableExceptionLayout.FaultPolicy] == 0) { matches++; }
        }
        return matches == 1 ? 1u : 0u;
    }

    private static uint ValidateFaultTraceReport(uint[] arena, uint descriptor, uint worker, uint report)
    {
        uint row = Report(arena, descriptor, worker, report), reference = row + WarpPortableExceptionLayout.ReportTrace;
        uint trace = RequireObject(arena, arena[reference], arena[reference + 1], arena[reference + 2]);
        if (trace == 0 || arena[trace + WarpPortableHeapLayout.SlotType] != arena[descriptor + WarpPortableExceptionLayout.TraceArrayType] ||
            arena[trace + WarpPortableHeapLayout.SlotKind] != WarpPortableHeapLayout.ValueArray ||
            arena[trace + WarpPortableHeapLayout.SlotLength] < WarpPortableExceptionTraceLayout.HeaderWords ||
            arena[trace + WarpPortableHeapLayout.SlotPayloadWords] < arena[trace + WarpPortableHeapLayout.SlotLength] ||
            Fits(arena[descriptor + WarpPortableExceptionLayout.MaximumFrames], WarpPortableExceptionTraceLayout.FrameWords * 2,
                arena[trace + WarpPortableHeapLayout.SlotLength] - WarpPortableExceptionTraceLayout.HeaderWords) == 0)
        { return WarpPortableExceptionLayout.InvalidReference; }
        uint root = arena[row + WarpPortableExceptionLayout.ReportRoot];
        if (OwnedRoot(arena, descriptor, root) == 0 || OwnedRoot(arena, descriptor, root + 1) == 0 ||
            FaultRootMatches(arena, root + 1, reference) == 0 ||
            (arena[Root(arena, root) + WarpPortableHeapLayout.RootReference] | arena[Root(arena, root) + WarpPortableHeapLayout.RootReference + 1] |
                arena[Root(arena, root) + WarpPortableHeapLayout.RootReference + 2]) != 0)
        { return WarpPortableExceptionLayout.InvalidOwnership; }
        return 0;
    }

    private static uint FaultRootMatches(uint[] arena, uint root, uint reference)
    {
        uint owner = Root(arena, root) + WarpPortableHeapLayout.RootReference;
        return arena[owner] == arena[reference] && arena[owner + 1] == arena[reference + 1] && arena[owner + 2] == arena[reference + 2] ? 1u : 0u;
    }

    private static uint FaultRecordRootsMatch(uint[] arena, uint descriptor, uint record)
    {
        uint root = arena[record + WarpPortableExceptionLayout.RecordRoot];
        return OwnedRoot(arena, descriptor, root) != 0 && OwnedRoot(arena, descriptor, root + 1) != 0 &&
            FaultRootMatches(arena, root, record + WarpPortableExceptionLayout.ExceptionReference) != 0 &&
            FaultRootMatches(arena, root + 1, record + WarpPortableExceptionLayout.TraceReference) != 0 ? 1u : 0u;
    }
}
