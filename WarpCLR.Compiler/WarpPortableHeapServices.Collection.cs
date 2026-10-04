namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    public static uint Collect(uint[] arena)
    {
        if (Begin(arena, 24) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (arena[WarpPortableHeapLayout.CollectionState] != WarpPortableHeapLayout.Requested ||
            arena[WarpPortableHeapLayout.ActiveWorkers] != arena[WarpPortableHeapLayout.ParkedWorkers] ||
            arena[WarpPortableHeapLayout.LeaseState] != 0 || arena[WarpPortableHeapLayout.PendingResult] != 0)
        {
            return Fail(arena, WarpPortableHeapLayout.Busy, arena[WarpPortableHeapLayout.ActiveWorkers], arena[WarpPortableHeapLayout.ParkedWorkers]);
        }
        arena[WarpPortableHeapLayout.CollectionState] = WarpPortableHeapLayout.Collecting;
        arena[WarpPortableHeapLayout.WorkCount] = 0;
        for (uint slot = 1; slot <= arena[WarpPortableHeapLayout.SlotCount]; slot++)
        {
            arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotMark] = 0;
        }
        uint fault = TraceRoots(arena);
        for (uint index = 0; fault == 0 && index < arena[WarpPortableHeapLayout.WorkCount]; index++)
        {
            fault = TraceObject(arena, arena[arena[WarpPortableHeapLayout.WorkStart] + index]);
        }
        if (fault != 0)
        {
            arena[WarpPortableHeapLayout.CollectionState] = WarpPortableHeapLayout.Requested;
            return fault;
        }
        uint released = 0;
        for (uint slot = 1; slot <= arena[WarpPortableHeapLayout.SlotCount]; slot++)
        {
            uint entry = Slot(arena, slot);
            if (arena[entry + WarpPortableHeapLayout.SlotState] == WarpPortableHeapLayout.Allocated && arena[entry + WarpPortableHeapLayout.SlotMark] == 0)
            {
                ReleaseObject(arena, slot);
                released++;
            }
        }
        Coalesce(arena);
        arena[WarpPortableHeapLayout.CollectionState] = WarpPortableHeapLayout.Idle;
        arena[WarpPortableHeapLayout.Result] = released;
        return 0;
    }

    private static uint TraceRoots(uint[] arena)
    {
        for (uint root = 1; root <= arena[WarpPortableHeapLayout.RootCount]; root++)
        {
            uint entry = Root(arena, root);
            uint reference = entry + WarpPortableHeapLayout.RootReference;
            if (arena[entry + WarpPortableHeapLayout.RootState] == WarpPortableHeapLayout.Allocated &&
                TraceReference(arena, arena[reference], arena[reference + 1], arena[reference + 2]) != 0)
            {
                return arena[WarpPortableHeapLayout.Fault];
            }
        }
        for (uint typeId = 1; typeId <= arena[WarpPortableHeapLayout.TypeCount]; typeId++)
        {
            uint type = Type(arena, typeId);
            if (TraceMap(arena, arena[type + WarpPortableHeapLayout.StaticStart],
                arena[type + WarpPortableHeapLayout.StaticReferenceMap], arena[type + WarpPortableHeapLayout.StaticReferenceCount]) != 0)
            {
                return arena[WarpPortableHeapLayout.Fault];
            }
            uint exception = type + WarpPortableHeapLayout.InitializerException;
            if (arena[type + WarpPortableHeapLayout.InitializerState] == 3 &&
                TraceReference(arena, arena[exception], arena[exception + 1], arena[exception + 2]) != 0)
            {
                return arena[WarpPortableHeapLayout.Fault];
            }
        }
        return 0;
    }

    private static uint TraceObject(uint[] arena, uint slot)
    {
        uint entry = Slot(arena, slot);
        uint payload = arena[entry + WarpPortableHeapLayout.SlotPayload];
        uint type = Type(arena, arena[entry + WarpPortableHeapLayout.SlotType]);
        uint kind = arena[entry + WarpPortableHeapLayout.SlotKind];
        if (kind == WarpPortableHeapLayout.ReferenceArray)
        {
            for (uint index = 0; index < arena[entry + WarpPortableHeapLayout.SlotLength]; index++)
            {
                uint reference = payload + index * 3;
                if (TraceReference(arena, arena[reference], arena[reference + 1], arena[reference + 2]) != 0)
                {
                    return arena[WarpPortableHeapLayout.Fault];
                }
            }
            return 0;
        }
        if (kind == WarpPortableHeapLayout.ValueArray)
        {
            uint element = Type(arena, arena[type + WarpPortableHeapLayout.ElementType]);
            for (uint index = 0; index < arena[entry + WarpPortableHeapLayout.SlotLength]; index++)
            {
                if (TraceMap(arena, payload + index * arena[type + WarpPortableHeapLayout.ElementWords],
                    arena[element + WarpPortableHeapLayout.ReferenceMap], arena[element + WarpPortableHeapLayout.ReferenceCount]) != 0)
                {
                    return arena[WarpPortableHeapLayout.Fault];
                }
            }
            return 0;
        }
        return TraceMap(arena, payload, arena[type + WarpPortableHeapLayout.ReferenceMap], arena[type + WarpPortableHeapLayout.ReferenceCount]);
    }

    private static uint TraceMap(uint[] arena, uint payload, uint map, uint count)
    {
        for (uint index = 0; index < count; index++)
        {
            uint reference = payload + arena[map + index * 2];
            if (TraceReference(arena, arena[reference], arena[reference + 1], arena[reference + 2]) != 0)
            {
                return arena[WarpPortableHeapLayout.Fault];
            }
        }
        return 0;
    }

    private static uint TraceReference(uint[] arena, uint context, uint slot, uint generation)
    {
        if (RequireReference(arena, context, slot, generation, 1) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (slot == 0 || arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotMark] != 0)
        {
            return 0;
        }
        arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotMark] = 1;
        arena[arena[WarpPortableHeapLayout.WorkStart] + arena[WarpPortableHeapLayout.WorkCount]] = slot;
        arena[WarpPortableHeapLayout.WorkCount]++;
        return 0;
    }
}
