namespace WarpCLR.Compiler;

internal static partial class WarpPortableExceptionServices
{
    private static uint Range(uint[] arena, uint start, uint count) => start <= (uint)arena.Length && count <= (uint)arena.Length - start ? 1u : 0u;
    private static uint Fits(uint count, uint width, uint available) => width != 0 &&
        WarpPortableWordMath.MultiplyHigh(count, width) == 0 && count * width <= available ? 1u : 0u;

    private static uint Descriptor(uint[] arena)
    {
        if ((uint)arena.Length < WarpPortableHeapLayout.HeaderWords || arena[0] != WarpPortableHeapLayout.Magic ||
            arena[1] != WarpPortableHeapLayout.Version || arena[3] != (uint)arena.Length)
        {
            return 0;
        }
        uint descriptor = arena[WarpPortableExceptionLayout.Descriptor];
        if (descriptor < WarpPortableHeapLayout.HeaderWords || Range(arena, descriptor, WarpPortableExceptionLayout.HeaderWords) == 0) { return 0; }
        uint end = arena[descriptor + WarpPortableExceptionLayout.End];
        if (arena[descriptor] != WarpPortableExceptionLayout.Magic || arena[descriptor + 1] != WarpPortableExceptionLayout.Version ||
            arena[descriptor + WarpPortableExceptionLayout.TraceVersion] != WarpPortableExceptionTraceLayout.Version ||
            end < WarpPortableExceptionLayout.HeaderWords || Range(arena, descriptor, end) == 0 ||
            descriptor + end > arena[WarpPortableHeapLayout.DataStart] ||
            arena[descriptor + WarpPortableExceptionLayout.WorkerCount] != arena[WarpPortableHeapLayout.WorkerCount] ||
            arena[descriptor + WarpPortableExceptionLayout.WorkerCount] == 0 ||
            arena[descriptor + WarpPortableExceptionLayout.MaximumFrames] == 0 ||
            arena[descriptor + WarpPortableExceptionLayout.RecordsPerWorker] == 0 ||
            arena[descriptor + WarpPortableExceptionLayout.ReportsPerWorker] == 0 || ValidateTables(arena, descriptor) == 0)
        {
            return 0;
        }
        return descriptor;
    }

    private static uint ValidateTables(uint[] arena, uint descriptor)
    {
        if (Table(arena, descriptor, WarpPortableExceptionLayout.BodyStart, WarpPortableExceptionLayout.BodyCount, WarpPortableExceptionLayout.BodyWords) == 0 ||
            Table(arena, descriptor, WarpPortableExceptionLayout.ClauseStart, WarpPortableExceptionLayout.ClauseCount, WarpPortableExceptionLayout.ClauseWords) == 0 ||
            Table(arena, descriptor, WarpPortableExceptionLayout.SiteStart, WarpPortableExceptionLayout.SiteCount, WarpPortableExceptionLayout.SiteWords) == 0 ||
            Table(arena, descriptor, WarpPortableExceptionLayout.PcStart, WarpPortableExceptionLayout.PcCount, WarpPortableExceptionLayout.PcWords) == 0 ||
            Table(arena, descriptor, WarpPortableExceptionLayout.FaultStart, WarpPortableExceptionLayout.FaultCount, WarpPortableExceptionLayout.FaultWords) == 0 ||
            Table(arena, descriptor, WarpPortableExceptionLayout.TemporaryOwnerStart, WarpPortableExceptionLayout.TemporaryOwnerCount, WarpPortableExceptionLayout.TemporaryOwnerWords) == 0 ||
            Table(arena, descriptor, WarpPortableExceptionLayout.WorkerStart, WarpPortableExceptionLayout.WorkerCount, WarpPortableExceptionLayout.WorkerWords) == 0) { return 0; }
        uint workers = arena[descriptor + WarpPortableExceptionLayout.WorkerCount];
        uint records = arena[descriptor + WarpPortableExceptionLayout.RecordsPerWorker];
        uint reports = arena[descriptor + WarpPortableExceptionLayout.ReportsPerWorker];
        uint frames = arena[descriptor + WarpPortableExceptionLayout.MaximumFrames];
        if (WarpPortableWordMath.MultiplyHigh(workers, records) != 0 || WarpPortableWordMath.MultiplyHigh(workers, reports) != 0 ||
            WarpPortableWordMath.MultiplyHigh(workers * records, frames) != 0 ||
            Region(arena, descriptor, arena[descriptor + WarpPortableExceptionLayout.RecordStart], workers * records, WarpPortableExceptionLayout.RecordWords) == 0 ||
            Region(arena, descriptor, arena[descriptor + WarpPortableExceptionLayout.FrameStart], workers * records * frames, WarpPortableExceptionLayout.FrameWords) == 0 ||
            Region(arena, descriptor, arena[descriptor + WarpPortableExceptionLayout.ReportStart], workers * reports, WarpPortableExceptionLayout.ReportWords) == 0 ||
            Region(arena, descriptor, arena[descriptor + WarpPortableExceptionLayout.ListStart], arena[descriptor + WarpPortableExceptionLayout.ListCount], 1) == 0 ||
            arena[descriptor + WarpPortableExceptionLayout.TraceGenerationCount] != arena[WarpPortableHeapLayout.SlotCount] ||
            Region(arena, descriptor, arena[descriptor + WarpPortableExceptionLayout.TraceGenerations], arena[descriptor + WarpPortableExceptionLayout.TraceGenerationCount], 1) == 0) { return 0; }
        return ValidateHeapTables(arena, descriptor);
    }

    private static uint Table(uint[] arena, uint descriptor, uint start, uint count, uint width) =>
        Region(arena, descriptor, arena[descriptor + start], arena[descriptor + count], width);

    private static uint Region(uint[] arena, uint descriptor, uint start, uint count, uint width) =>
        start >= WarpPortableExceptionLayout.HeaderWords && start <= arena[descriptor + WarpPortableExceptionLayout.End] &&
        Fits(count, width, arena[descriptor + WarpPortableExceptionLayout.End] - start) != 0 ? 1u : 0u;

    private static uint ValidateHeapTables(uint[] arena, uint descriptor)
    {
        uint types = arena[WarpPortableHeapLayout.TypeCount]; uint roots = arena[WarpPortableHeapLayout.RootCount];
        if (types == 0 || Range(arena, arena[WarpPortableHeapLayout.TypeStart], 0) == 0 || Fits(types, WarpPortableHeapLayout.TypeWords, (uint)arena.Length - arena[WarpPortableHeapLayout.TypeStart]) == 0 ||
            WarpPortableWordMath.MultiplyHigh(types, types) != 0 || Range(arena, arena[WarpPortableHeapLayout.ClosureStart], types * types) == 0 ||
            Range(arena, arena[WarpPortableHeapLayout.SlotStart], 0) == 0 || Fits(arena[WarpPortableHeapLayout.SlotCount], WarpPortableHeapLayout.SlotWords, (uint)arena.Length - arena[WarpPortableHeapLayout.SlotStart]) == 0 ||
            Range(arena, arena[WarpPortableHeapLayout.RootStart], 0) == 0 || Fits(roots, WarpPortableHeapLayout.RootWords, (uint)arena.Length - arena[WarpPortableHeapLayout.RootStart]) == 0 ||
            arena[descriptor + WarpPortableExceptionLayout.ExceptionType] == 0 || arena[descriptor + WarpPortableExceptionLayout.ExceptionType] > types ||
            arena[descriptor + WarpPortableExceptionLayout.TraceArrayType] == 0 || arena[descriptor + WarpPortableExceptionLayout.TraceArrayType] > types)
        {
            return 0;
        }
        uint first = arena[descriptor + WarpPortableExceptionLayout.RootFirst]; uint count = arena[descriptor + WarpPortableExceptionLayout.RootCount];
        if (first == 0 || first > roots || count > roots - first + 1) { return 0; }
        for (uint root = first; root < first + count; root++)
        {
            uint row = Root(arena, root);
            if (arena[row + WarpPortableHeapLayout.RootState] != WarpPortableHeapLayout.Allocated ||
                arena[row + WarpPortableHeapLayout.RootOwnership] != WarpPortableHeapLayout.RuntimeOwnedRoot ||
                arena[row + WarpPortableHeapLayout.RootKind] != WarpPortableHeapLayout.StrongRoot) { return 0; }
        }
        return ValidateRootAssignments(arena, descriptor, first, count);
    }

    private static uint ValidateRootAssignments(uint[] arena, uint descriptor, uint first, uint count)
    {
        uint records = arena[descriptor + WarpPortableExceptionLayout.RecordsPerWorker];
        uint reports = arena[descriptor + WarpPortableExceptionLayout.ReportsPerWorker];
        uint workers = arena[descriptor + WarpPortableExceptionLayout.WorkerCount];
        if (records > (count >> 1) || reports > (count >> 1) || records + reports > (count >> 1) ||
            WarpPortableWordMath.MultiplyHigh(workers, (records + reports) * 2) != 0 ||
            workers * (records + reports) * 2 != count) { return 0; }
        for (uint worker = 0; worker < workers; worker++)
        {
            uint expected = first + worker * (records + reports) * 2;
            for (uint record = 1; record <= records; record++)
            {
                if (arena[Record(arena, descriptor, worker, record) + WarpPortableExceptionLayout.RecordRoot] != expected + (record - 1) * 2) { return 0; }
            }
            for (uint report = 1; report <= reports; report++)
            {
                if (arena[Report(arena, descriptor, worker, report) + WarpPortableExceptionLayout.ReportRoot] != expected + (records + report - 1) * 2) { return 0; }
            }
        }
        return 1;
    }

    private static uint RequireObject(uint[] arena, uint context, uint slot, uint generation)
    {
        if (context != arena[WarpPortableHeapLayout.Context] || slot == 0 || slot > arena[WarpPortableHeapLayout.SlotCount] || generation == 0) { return 0; }
        uint row = arena[WarpPortableHeapLayout.SlotStart] + (slot - 1) * WarpPortableHeapLayout.SlotWords;
        return arena[row + WarpPortableHeapLayout.SlotState] == WarpPortableHeapLayout.Allocated &&
            arena[row + WarpPortableHeapLayout.SlotGeneration] == generation &&
            arena[row + WarpPortableHeapLayout.SlotType] != 0 && arena[row + WarpPortableHeapLayout.SlotType] <= arena[WarpPortableHeapLayout.TypeCount] &&
            arena[row + WarpPortableHeapLayout.SlotPayload] >= arena[WarpPortableHeapLayout.DataStart] &&
            Range(arena, arena[row + WarpPortableHeapLayout.SlotPayload], arena[row + WarpPortableHeapLayout.SlotPayloadWords]) != 0 ? row : 0;
    }

    private static uint RequireException(uint[] arena, uint descriptor, uint context, uint slot, uint generation, uint exactType)
    {
        uint row = RequireObject(arena, context, slot, generation);
        return row != 0 && arena[row + WarpPortableHeapLayout.SlotType] == exactType &&
            arena[row + WarpPortableHeapLayout.SlotPayloadWords] >= WarpPortableSourceExceptionLayout.PrefixWords &&
            Assignable(arena, exactType, arena[descriptor + WarpPortableExceptionLayout.ExceptionType]) != 0 ? row : 0;
    }

    private static uint Assignable(uint[] arena, uint source, uint target) => source != 0 && source <= arena[WarpPortableHeapLayout.TypeCount] &&
        target != 0 && target <= arena[WarpPortableHeapLayout.TypeCount] ?
        arena[arena[WarpPortableHeapLayout.ClosureStart] + (source - 1) * arena[WarpPortableHeapLayout.TypeCount] + target - 1] : 0;

    private static uint LookupSite(uint[] arena, uint descriptor, uint function, uint offset, uint opcode)
    {
        for (uint site = 1; site <= arena[descriptor + WarpPortableExceptionLayout.SiteCount]; site++)
        {
            uint row = Site(arena, descriptor, site);
            if (arena[row + WarpPortableExceptionLayout.SiteFunction] == function && arena[row + WarpPortableExceptionLayout.SiteOffset] == offset &&
                arena[row + WarpPortableExceptionLayout.SiteOpCode] == opcode) { return site; }
        }
        return 0;
    }

    private static uint LookupPc(uint[] arena, uint descriptor, uint function, uint pc)
    {
        for (uint index = 0; index < arena[descriptor + WarpPortableExceptionLayout.PcCount]; index++)
        {
            uint row = descriptor + arena[descriptor + WarpPortableExceptionLayout.PcStart] + index * WarpPortableExceptionLayout.PcWords;
            if (arena[row + WarpPortableExceptionLayout.PcValue] == pc && arena[row + WarpPortableExceptionLayout.PcFunction] == function)
            {
                uint site = arena[row + WarpPortableExceptionLayout.PcSite];
                return site != 0 && site <= arena[descriptor + WarpPortableExceptionLayout.SiteCount] ? site : 0;
            }
        }
        return 0;
    }

    private static uint LookupBody(uint[] arena, uint descriptor, uint function)
    {
        for (uint index = 0; index < arena[descriptor + WarpPortableExceptionLayout.BodyCount]; index++)
        {
            uint row = descriptor + arena[descriptor + WarpPortableExceptionLayout.BodyStart] + index * WarpPortableExceptionLayout.BodyWords;
            if (arena[row + WarpPortableExceptionLayout.BodyFunction] == function) { return row; }
        }
        return 0;
    }

    private static uint ValidList(uint[] arena, uint descriptor, uint start, uint count)
    {
        uint first = arena[descriptor + WarpPortableExceptionLayout.ListStart]; uint words = arena[descriptor + WarpPortableExceptionLayout.ListCount];
        return start >= first && start - first <= words && count <= words - (start - first) ? 1u : 0u;
    }
}
