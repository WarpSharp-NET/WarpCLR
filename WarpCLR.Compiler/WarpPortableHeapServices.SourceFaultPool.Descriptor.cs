namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    private static uint SourceFaultPoolDescriptor(uint[] arena)
    {
        uint length = (uint)arena.Length, descriptor = arena[WarpPortableSourceFaultFactoryLayout.Descriptor];
        if (descriptor < WarpPortableHeapLayout.HeaderWords || descriptor > length || length - descriptor < WarpPortableSourceFaultFactoryLayout.HeaderWords ||
            arena[descriptor] != WarpPortableSourceFaultFactoryLayout.Magic || arena[descriptor + 1] != WarpPortableSourceFaultFactoryLayout.Version ||
            arena[descriptor + WarpPortableSourceFaultFactoryLayout.Context] != arena[WarpPortableHeapLayout.Context])
        {
            Fail(arena, WarpPortableHeapLayout.InvalidType, descriptor, 0); return 0;
        }
        uint words = arena[descriptor + WarpPortableSourceFaultFactoryLayout.DescriptorWords];
        uint memory = SourceViewDescriptor(arena);
        if (memory == 0 || words < WarpPortableSourceFaultFactoryLayout.HeaderWords || words > length - descriptor ||
            descriptor + words > arena[WarpPortableHeapLayout.DataStart] || RequireFaultPoolTables(arena, descriptor, words) != 0)
        {
            Fail(arena, WarpPortableHeapLayout.InvalidType, descriptor, words); return 0;
        }
        for (uint word = 0; word < 8; word++)
        {
            if (arena[descriptor + WarpPortableSourceFaultFactoryLayout.SchemaHash + word] != arena[memory + WarpPortableSourceMemoryLayout.Hash + word])
            {
                Fail(arena, WarpPortableHeapLayout.InvalidType, descriptor, word); return 0;
            }
        }
        uint type = arena[descriptor + WarpPortableSourceFaultFactoryLayout.StringType];
        if (RequireType(arena, type) != 0 || arena[Type(arena, type) + WarpPortableHeapLayout.TypeKind] != WarpPortableHeapLayout.String)
        {
            Fail(arena, WarpPortableHeapLayout.InvalidType, type, 0); return 0;
        }
        return descriptor;
    }

    private static uint RequireFaultPoolTables(uint[] arena, uint descriptor, uint words)
    {
        uint end = descriptor + words, rows = arena[descriptor + WarpPortableSourceFaultFactoryLayout.RowStart];
        uint resources = arena[descriptor + WarpPortableSourceFaultFactoryLayout.ResourceStart];
        uint prepared = arena[descriptor + WarpPortableSourceFaultFactoryLayout.PreparedStart], text = arena[descriptor + WarpPortableSourceFaultFactoryLayout.TextStart];
        uint count = arena[descriptor + WarpPortableSourceFaultFactoryLayout.RowCount], reports = arena[descriptor + WarpPortableSourceFaultFactoryLayout.ReportsPerRow];
        uint preparedCount = arena[descriptor + WarpPortableSourceFaultFactoryLayout.PreparedCount], resourceCount = arena[descriptor + WarpPortableSourceFaultFactoryLayout.ResourceCount];
        if (rows != descriptor + WarpPortableSourceFaultFactoryLayout.HeaderWords || resources < rows || prepared < resources || text < prepared || text > end ||
            count == 0 || reports == 0 || count > WarpPortableInteger32.DivideUnsigned(resources - rows, WarpPortableSourceFaultFactoryLayout.RowWords) ||
            count * WarpPortableSourceFaultFactoryLayout.RowWords != resources - rows ||
            resourceCount > WarpPortableInteger32.DivideUnsigned(prepared - resources, WarpPortableSourceFaultFactoryLayout.ResourceWords) || resourceCount * WarpPortableSourceFaultFactoryLayout.ResourceWords != prepared - resources ||
            preparedCount > WarpPortableInteger32.DivideUnsigned(text - prepared, WarpPortableSourceFaultFactoryLayout.PreparedWords) || preparedCount * WarpPortableSourceFaultFactoryLayout.PreparedWords != text - prepared ||
            WarpPortableWordMath.MultiplyHigh(count, reports) != 0 || count * reports != preparedCount ||
            arena[descriptor + WarpPortableSourceFaultFactoryLayout.TextUnits] != end - text || resourceCount > 0xFFFFFFFFu - preparedCount ||
            arena[descriptor + WarpPortableSourceFaultFactoryLayout.RootCount] != resourceCount + preparedCount)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidType, descriptor, words);
        }
        uint first = arena[descriptor + WarpPortableSourceFaultFactoryLayout.RootFirst], roots = arena[WarpPortableHeapLayout.RootCount];
        uint rootStart = arena[WarpPortableHeapLayout.RootStart];
        return first == 0 || first > roots || resourceCount + preparedCount > roots - first + 1 || rootStart > (uint)arena.Length ||
            roots > WarpPortableInteger32.DivideUnsigned((uint)arena.Length - rootStart, WarpPortableHeapLayout.RootWords) ? Fail(arena, WarpPortableHeapLayout.InvalidType, first, roots) : 0;
    }

    private static uint SourceFaultResourceRow(uint[] arena, uint descriptor, uint id)
    {
        if (id == 0 || id > arena[descriptor + WarpPortableSourceFaultFactoryLayout.ResourceCount])
        {
            Fail(arena, WarpPortableHeapLayout.Bounds, id, arena[descriptor + WarpPortableSourceFaultFactoryLayout.ResourceCount]); return 0;
        }
        uint row = arena[descriptor + WarpPortableSourceFaultFactoryLayout.ResourceStart] + (id - 1) * WarpPortableSourceFaultFactoryLayout.ResourceWords;
        uint text = arena[row + WarpPortableSourceFaultFactoryLayout.ResourceText], start = arena[descriptor + WarpPortableSourceFaultFactoryLayout.TextStart];
        uint end = start + arena[descriptor + WarpPortableSourceFaultFactoryLayout.TextUnits];
        if (text < start || text > end || arena[row + WarpPortableSourceFaultFactoryLayout.ResourceLength] > end - text)
        {
            Fail(arena, WarpPortableHeapLayout.InvalidType, id, text); return 0;
        }
        if (arena[row + WarpPortableSourceFaultFactoryLayout.ResourceRoot] != arena[descriptor + WarpPortableSourceFaultFactoryLayout.RootFirst] + id - 1 ||
            arena[row + WarpPortableSourceFaultFactoryLayout.ResourceRootGeneration] != 1)
        {
            Fail(arena, WarpPortableHeapLayout.InvalidReference, id, arena[row + WarpPortableSourceFaultFactoryLayout.ResourceRoot]); return 0;
        }
        return row;
    }

    private static uint SourceFaultPreparedRow(uint[] arena, uint descriptor, uint index)
    {
        if (index >= arena[descriptor + WarpPortableSourceFaultFactoryLayout.PreparedCount])
        {
            Fail(arena, WarpPortableHeapLayout.Bounds, index, arena[descriptor + WarpPortableSourceFaultFactoryLayout.PreparedCount]); return 0;
        }
        uint row = arena[descriptor + WarpPortableSourceFaultFactoryLayout.PreparedStart] + index * WarpPortableSourceFaultFactoryLayout.PreparedWords;
        if (arena[row + WarpPortableSourceFaultFactoryLayout.PreparedFactoryRow] != WarpPortableInteger32.DivideUnsigned(index, arena[descriptor + WarpPortableSourceFaultFactoryLayout.ReportsPerRow]) + 1 ||
            arena[row + WarpPortableSourceFaultFactoryLayout.PreparedRoot] != arena[descriptor + WarpPortableSourceFaultFactoryLayout.RootFirst] +
                arena[descriptor + WarpPortableSourceFaultFactoryLayout.ResourceCount] + index || arena[row + WarpPortableSourceFaultFactoryLayout.PreparedRootGeneration] != 1)
        {
            Fail(arena, WarpPortableHeapLayout.InvalidReference, index, arena[row + WarpPortableSourceFaultFactoryLayout.PreparedRoot]); return 0;
        }
        return row;
    }
}
