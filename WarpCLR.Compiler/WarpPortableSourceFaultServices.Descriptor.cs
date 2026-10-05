namespace WarpCLR.Compiler;

internal static partial class WarpPortableSourceFaultServices
{
    private static uint Range(uint[] arena, uint start, uint count) => start <= (uint)arena.Length && count <= (uint)arena.Length - start ? 1u : 0u;
    private static uint Fits(uint count, uint width, uint available) => WarpPortableWordMath.MultiplyHigh(count, width) == 0 && count * width <= available ? 1u : 0u;
    private static uint Prepared(uint[] arena, uint descriptor, uint index) => arena[descriptor + WarpPortableSourceFaultFactoryLayout.PreparedStart] + index * WarpPortableSourceFaultFactoryLayout.PreparedWords;
    private static uint Factory(uint[] arena, uint descriptor, uint id) => arena[descriptor + WarpPortableSourceFaultFactoryLayout.RowStart] + (id - 1) * WarpPortableSourceFaultFactoryLayout.RowWords;
    private static uint Root(uint[] arena, uint root) => arena[WarpPortableHeapLayout.RootStart] + (root - 1) * WarpPortableHeapLayout.RootWords;
    private static uint ExceptionRecord(uint[] arena, uint descriptor, uint worker, uint index) => descriptor + arena[descriptor + WarpPortableExceptionLayout.RecordStart] +
        (worker * arena[descriptor + WarpPortableExceptionLayout.RecordsPerWorker] + index - 1) * WarpPortableExceptionLayout.RecordWords;

    private static uint Descriptor(uint[] arena)
    {
        uint descriptor = arena[WarpPortableSourceFaultFactoryLayout.Descriptor], data = arena[WarpPortableHeapLayout.DataStart];
        if (descriptor < WarpPortableHeapLayout.HeaderWords || Range(arena, descriptor, WarpPortableSourceFaultFactoryLayout.HeaderWords) == 0 ||
            arena[descriptor] != WarpPortableSourceFaultFactoryLayout.Magic || arena[descriptor + 1] != WarpPortableSourceFaultFactoryLayout.Version ||
            arena[descriptor + WarpPortableSourceFaultFactoryLayout.Context] != arena[WarpPortableHeapLayout.Context]) { return 0; }
        uint words = arena[descriptor + WarpPortableSourceFaultFactoryLayout.DescriptorWords];
        if (words < WarpPortableSourceFaultFactoryLayout.HeaderWords || Range(arena, descriptor, words) == 0 || descriptor + words > data) { return 0; }
        uint rows = arena[descriptor + WarpPortableSourceFaultFactoryLayout.RowStart], resources = arena[descriptor + WarpPortableSourceFaultFactoryLayout.ResourceStart];
        uint prepared = arena[descriptor + WarpPortableSourceFaultFactoryLayout.PreparedStart], text = arena[descriptor + WarpPortableSourceFaultFactoryLayout.TextStart];
        uint count = arena[descriptor + WarpPortableSourceFaultFactoryLayout.RowCount], resourceCount = arena[descriptor + WarpPortableSourceFaultFactoryLayout.ResourceCount];
        uint preparedCount = arena[descriptor + WarpPortableSourceFaultFactoryLayout.PreparedCount], reports = arena[descriptor + WarpPortableSourceFaultFactoryLayout.ReportsPerRow];
        if (count == 0 || reports == 0 || rows != descriptor + WarpPortableSourceFaultFactoryLayout.HeaderWords || resources < rows || prepared < resources || text < prepared ||
            text > descriptor + words || Fits(count, WarpPortableSourceFaultFactoryLayout.RowWords, resources - rows) == 0 ||
            count * WarpPortableSourceFaultFactoryLayout.RowWords != resources - rows ||
            Fits(resourceCount, WarpPortableSourceFaultFactoryLayout.ResourceWords, prepared - resources) == 0 ||
            resourceCount * WarpPortableSourceFaultFactoryLayout.ResourceWords != prepared - resources ||
            Fits(preparedCount, WarpPortableSourceFaultFactoryLayout.PreparedWords, text - prepared) == 0 ||
            preparedCount * WarpPortableSourceFaultFactoryLayout.PreparedWords != text - prepared ||
            WarpPortableWordMath.MultiplyHigh(count, reports) != 0 || count * reports != preparedCount ||
            arena[descriptor + WarpPortableSourceFaultFactoryLayout.TextUnits] != descriptor + words - text || resourceCount > uint.MaxValue - preparedCount ||
            arena[descriptor + WarpPortableSourceFaultFactoryLayout.RootCount] != resourceCount + preparedCount) { return 0; }
        uint first = arena[descriptor + WarpPortableSourceFaultFactoryLayout.RootFirst], roots = arena[WarpPortableHeapLayout.RootCount];
        if (first == 0 || first > roots || resourceCount + preparedCount > roots - first + 1) { return 0; }
        uint eh = arena[WarpPortableExceptionLayout.Descriptor], ehFirst = arena[eh + WarpPortableExceptionLayout.RootFirst];
        uint ehCount = arena[eh + WarpPortableExceptionLayout.RootCount];
        if (first < ehFirst ? resourceCount + preparedCount > ehFirst - first : first - ehFirst < ehCount) { return 0; }
        uint memory = MemoryDescriptor(arena);
        if (memory == 0 || HashMatches(arena, descriptor + WarpPortableSourceFaultFactoryLayout.SchemaHash, memory + WarpPortableSourceMemoryLayout.Hash) == 0 ||
            HashMatches(arena, descriptor + WarpPortableSourceFaultFactoryLayout.SchemaHash, eh + WarpPortableExceptionLayout.SchemaHash) == 0) { return 0; }
        uint type = arena[descriptor + WarpPortableSourceFaultFactoryLayout.StringType];
        if (type == 0 || type > arena[WarpPortableHeapLayout.TypeCount] ||
            arena[arena[WarpPortableHeapLayout.TypeStart] + (type - 1) * WarpPortableHeapLayout.TypeWords + WarpPortableHeapLayout.TypeKind] != WarpPortableHeapLayout.String)
        { return 0; }
        return descriptor;
    }

    private static uint MemoryDescriptor(uint[] arena)
    {
        uint memory = arena[WarpPortableSourceMemoryLayout.Descriptor], data = arena[WarpPortableHeapLayout.DataStart];
        if (memory < WarpPortableHeapLayout.HeaderWords || Range(arena, memory, WarpPortableSourceMemoryLayout.HeaderWords) == 0 ||
            memory + WarpPortableSourceMemoryLayout.HeaderWords > data || arena[memory] != WarpPortableSourceMemoryLayout.Magic ||
            arena[memory + 1] != WarpPortableSourceMemoryLayout.Version || arena[memory + WarpPortableSourceMemoryLayout.TypeCount] != arena[WarpPortableHeapLayout.TypeCount]) { return 0; }
        uint start = arena[memory + WarpPortableSourceMemoryLayout.ExceptionTypeStart], types = arena[WarpPortableHeapLayout.TypeCount], slots = arena[WarpPortableHeapLayout.SlotCount];
        if (start < memory + WarpPortableSourceMemoryLayout.HeaderWords || start > data || Fits(types, WarpPortableSourceExceptionLayout.ExceptionTypeWords, data - start) == 0)
        { return 0; }
        uint states = start + types * WarpPortableSourceExceptionLayout.ExceptionTypeWords;
        return Fits(slots, WarpPortableSourceExceptionLayout.DataStateWords, data - states) != 0 ? memory : 0;
    }

    private static uint HashMatches(uint[] arena, uint first, uint second)
    {
        uint nonzero = 0;
        for (uint word = 0; word < 8; word++)
        {
            if (arena[first + word] != arena[second + word]) { return 0; }
            nonzero |= arena[first + word];
        }
        return nonzero != 0 ? 1u : 0u;
    }

    private static uint Object(uint[] arena, uint context, uint slot, uint generation)
    {
        if (context != arena[WarpPortableHeapLayout.Context] || slot == 0 || slot > arena[WarpPortableHeapLayout.SlotCount] || generation == 0) { return 0; }
        uint entry = arena[WarpPortableHeapLayout.SlotStart] + (slot - 1) * WarpPortableHeapLayout.SlotWords;
        return arena[entry + WarpPortableHeapLayout.SlotState] == WarpPortableHeapLayout.Allocated && arena[entry + WarpPortableHeapLayout.SlotGeneration] == generation &&
            arena[entry + WarpPortableHeapLayout.SlotType] != 0 && arena[entry + WarpPortableHeapLayout.SlotType] <= arena[WarpPortableHeapLayout.TypeCount] &&
            arena[entry + WarpPortableHeapLayout.SlotPayload] >= arena[WarpPortableHeapLayout.DataStart] &&
            Range(arena, arena[entry + WarpPortableHeapLayout.SlotPayload], arena[entry + WarpPortableHeapLayout.SlotPayloadWords]) != 0 ? entry : 0;
    }
}
