namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    private static uint RequireFaultPoolRoot(uint[] arena, uint descriptor, uint root, uint rootGeneration, uint context, uint slot, uint generation, uint empty)
    {
        uint first = arena[descriptor + WarpPortableSourceFaultFactoryLayout.RootFirst], count = arena[descriptor + WarpPortableSourceFaultFactoryLayout.RootCount];
        if (root < first || root - first >= count || rootGeneration == 0) { return Fail(arena, WarpPortableHeapLayout.InvalidReference, root, rootGeneration); }
        uint entry = Root(arena, root);
        if (arena[entry + WarpPortableHeapLayout.RootOwnership] != WarpPortableHeapLayout.RuntimeOwnedRoot ||
            arena[entry + WarpPortableHeapLayout.RootGeneration] != rootGeneration ||
            arena[entry + WarpPortableHeapLayout.RootState] != (empty != 0 ? WarpPortableHeapLayout.Free : WarpPortableHeapLayout.Allocated))
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidReference, root, rootGeneration);
        }
        if (empty == 0 && (arena[entry + WarpPortableHeapLayout.RootKind] != WarpPortableHeapLayout.StrongRoot ||
            arena[entry + WarpPortableHeapLayout.RootReference] != context || arena[entry + WarpPortableHeapLayout.RootReference + 1] != slot ||
            arena[entry + WarpPortableHeapLayout.RootReference + 2] != generation))
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidReference, root, slot);
        }
        if (arena[entry + WarpPortableHeapLayout.RootInteriorOffset] != 0 || arena[entry + WarpPortableHeapLayout.RootInteriorWords] != 0 ||
            arena[entry + WarpPortableHeapLayout.RootInteriorType] != 0 || empty != 0 &&
                (arena[entry + WarpPortableHeapLayout.RootKind] != 0 || (arena[entry + WarpPortableHeapLayout.RootReference] |
                    arena[entry + WarpPortableHeapLayout.RootReference + 1] | arena[entry + WarpPortableHeapLayout.RootReference + 2]) != 0))
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidReference, root, slot);
        }
        return 0;
    }

    private static uint StoreFaultPoolReference(uint[] arena, uint reference, uint root)
    {
        uint entry = Root(arena, root);
        for (uint word = 0; word < 3; word++)
        {
            uint value = arena[WarpPortableHeapLayout.Result + word];
            arena[reference + word] = value; arena[entry + WarpPortableHeapLayout.RootReference + word] = value;
        }
        arena[entry + WarpPortableHeapLayout.RootKind] = WarpPortableHeapLayout.StrongRoot;
        arena[entry + WarpPortableHeapLayout.RootState] = WarpPortableHeapLayout.Allocated;
        arena[WarpPortableHeapLayout.LiveRoots]++;
        return 0;
    }

    private static uint RequireEmptyFaultResource(uint[] arena, uint descriptor, uint row)
    {
        uint reference = row + WarpPortableSourceFaultFactoryLayout.ResourceReference;
        if (arena[row + WarpPortableSourceFaultFactoryLayout.ResourceState] != WarpPortableSourceFaultFactoryLayout.Empty ||
            (arena[reference] | arena[reference + 1] | arena[reference + 2]) != 0)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, row, arena[row + WarpPortableSourceFaultFactoryLayout.ResourceState]);
        }
        return RequireFaultPoolRoot(arena, descriptor, arena[row + WarpPortableSourceFaultFactoryLayout.ResourceRoot],
            arena[row + WarpPortableSourceFaultFactoryLayout.ResourceRootGeneration], 0, 0, 0, 1);
    }

    private static uint RequireEmptyFaultRecord(uint[] arena, uint descriptor, uint record)
    {
        uint reference = record + WarpPortableSourceFaultFactoryLayout.PreparedReference;
        if (arena[record] != WarpPortableSourceFaultFactoryLayout.Empty || (arena[reference] | arena[reference + 1] | arena[reference + 2]) != 0)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, record, arena[record]);
        }
        for (uint word = WarpPortableSourceFaultFactoryLayout.PreparedTicketGeneration; word <= WarpPortableSourceFaultFactoryLayout.PreparedExceptionRootGeneration; word++)
        {
            if (arena[record + word] != 0) { return Fail(arena, WarpPortableHeapLayout.InvalidOperation, record, word); }
        }
        for (uint word = WarpPortableSourceFaultFactoryLayout.PreparedController; word < WarpPortableSourceFaultFactoryLayout.PreparedWords; word++)
        {
            if (arena[record + word] != 0) { return Fail(arena, WarpPortableHeapLayout.InvalidOperation, record, word); }
        }
        return RequireFaultPoolRoot(arena, descriptor, arena[record + WarpPortableSourceFaultFactoryLayout.PreparedRoot],
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedRootGeneration], 0, 0, 0, 1);
    }
}
