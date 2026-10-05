namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    private static uint RequireFaultResourcesReady(uint[] arena, uint descriptor, uint row)
    {
        uint message = arena[row + WarpPortableSourceFaultFactoryLayout.RowMessage], param = arena[row + WarpPortableSourceFaultFactoryLayout.RowParamName];
        return RequireReadyFaultResource(arena, descriptor, message) != 0 || param != 0 && RequireReadyFaultResource(arena, descriptor, param) != 0 ?
            arena[WarpPortableHeapLayout.Fault] : 0;
    }

    private static uint RequireReadyFaultResource(uint[] arena, uint descriptor, uint id)
    {
        uint row = SourceFaultResourceRow(arena, descriptor, id);
        if (row == 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint reference = row + WarpPortableSourceFaultFactoryLayout.ResourceReference;
        if (arena[row + WarpPortableSourceFaultFactoryLayout.ResourceState] != WarpPortableSourceFaultFactoryLayout.Ready ||
            RequireReference(arena, arena[reference], arena[reference + 1], arena[reference + 2], 0) != 0 ||
            RequireFaultPoolRoot(arena, descriptor, arena[row + WarpPortableSourceFaultFactoryLayout.ResourceRoot],
                arena[row + WarpPortableSourceFaultFactoryLayout.ResourceRootGeneration], arena[reference], arena[reference + 1], arena[reference + 2], 0) != 0)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidReference, id, arena[row + WarpPortableSourceFaultFactoryLayout.ResourceState]);
        }
        uint slot = Slot(arena, arena[reference + 1]), length = arena[row + WarpPortableSourceFaultFactoryLayout.ResourceLength];
        uint payload = arena[slot + WarpPortableHeapLayout.SlotPayload], text = arena[row + WarpPortableSourceFaultFactoryLayout.ResourceText];
        if (arena[slot + WarpPortableHeapLayout.SlotType] != arena[descriptor + WarpPortableSourceFaultFactoryLayout.StringType] ||
            arena[slot + WarpPortableHeapLayout.SlotKind] != WarpPortableHeapLayout.String || arena[slot + WarpPortableHeapLayout.SlotLength] != length ||
            payload < arena[WarpPortableHeapLayout.DataStart] || payload > (uint)arena.Length || ((length >> 1) + (length & 1u)) > (uint)arena.Length - payload)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidType, id, length);
        }
        for (uint index = 0; index < length; index++)
        {
            if (((arena[payload + (index >> 1)] >> (int)((index & 1u) * 16)) & 0xFFFFu) != arena[text + index])
            {
                return Fail(arena, WarpPortableHeapLayout.InvalidOperation, id, index);
            }
        }
        return 0;
    }

    private static uint CopyFaultResource(uint[] arena, uint descriptor, uint id, uint destination)
    {
        if (id == 0) { return 0; }
        uint source = arena[descriptor + WarpPortableSourceFaultFactoryLayout.ResourceStart] + (id - 1) * WarpPortableSourceFaultFactoryLayout.ResourceWords +
            WarpPortableSourceFaultFactoryLayout.ResourceReference;
        for (uint word = 0; word < 3; word++) { arena[destination + word] = arena[source + word]; }
        return 0;
    }

    private static uint FaultResourceMatches(uint[] arena, uint descriptor, uint id, uint destination)
    {
        if (id == 0) { return (arena[destination] | arena[destination + 1] | arena[destination + 2]) == 0 ? 1u : 0u; }
        uint source = arena[descriptor + WarpPortableSourceFaultFactoryLayout.ResourceStart] + (id - 1) * WarpPortableSourceFaultFactoryLayout.ResourceWords +
            WarpPortableSourceFaultFactoryLayout.ResourceReference;
        return arena[destination] == arena[source] && arena[destination + 1] == arena[source + 1] && arena[destination + 2] == arena[source + 2] ? 1u : 0u;
    }
}
