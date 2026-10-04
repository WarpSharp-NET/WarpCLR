namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    public static uint BeginTypeInitialization(uint[] arena, uint typeId, uint worker)
    {
        if (Begin(arena, 25) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequireType(arena, typeId) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (worker >= arena[WarpPortableHeapLayout.WorkerCount])
        {
            return Fail(arena, WarpPortableHeapLayout.Quota, worker, arena[WarpPortableHeapLayout.WorkerCount]);
        }
        uint type = Type(arena, typeId);
        uint state = arena[type + WarpPortableHeapLayout.InitializerState];
        if (state == 0)
        {
            arena[type + WarpPortableHeapLayout.InitializerState] = 1;
            arena[type + WarpPortableHeapLayout.InitializerOwner] = worker;
            arena[WarpPortableHeapLayout.Result] = 1;
            return 0;
        }
        if (state == 1)
        {
            if (arena[type + WarpPortableHeapLayout.InitializerOwner] != worker)
            {
                return Fail(arena, WarpPortableHeapLayout.Busy, typeId, arena[type + WarpPortableHeapLayout.InitializerOwner]);
            }
            arena[WarpPortableHeapLayout.Result] = 2;
            return 0;
        }
        if (state == 2)
        {
            arena[WarpPortableHeapLayout.Result] = 3;
            return 0;
        }
        uint exception = type + WarpPortableHeapLayout.InitializerException;
        SetReferenceResult(arena, arena[exception], arena[exception + 1], arena[exception + 2]);
        return Fail(arena, WarpPortableHeapLayout.TypeInitializationFailed, typeId, 0);
    }

    public static uint CompleteTypeInitialization(uint[] arena, uint typeId, uint worker)
    {
        if (Begin(arena, 26) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequireInitializerOwner(arena, typeId, worker) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        arena[Type(arena, typeId) + WarpPortableHeapLayout.InitializerState] = 2;
        return 0;
    }

    public static uint FailTypeInitialization(uint[] arena, uint typeId, uint worker, uint context, uint slot, uint generation)
    {
        if (Begin(arena, 27) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequireInitializerOwner(arena, typeId, worker) != 0 || RequireReference(arena, context, slot, generation, 0) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint type = Type(arena, typeId);
        arena[type + WarpPortableHeapLayout.InitializerState] = 3;
        uint exception = type + WarpPortableHeapLayout.InitializerException;
        arena[exception] = context;
        arena[exception + 1] = slot;
        arena[exception + 2] = generation;
        return 0;
    }

    public static uint WriteStaticReference(uint[] arena, uint typeId, uint offset, uint context, uint slot, uint generation)
    {
        if (Begin(arena, 28) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequireType(arena, typeId) != 0 || RequireReference(arena, context, slot, generation, 1) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint type = Type(arena, typeId);
        uint map = arena[type + WarpPortableHeapLayout.StaticReferenceMap];
        uint count = arena[type + WarpPortableHeapLayout.StaticReferenceCount];
        uint targetType = 0;
        for (uint index = 0; index < count; index++)
        {
            if (arena[map + index * 2] == offset)
            {
                targetType = arena[map + index * 2 + 1];
            }
        }
        if (targetType == 0)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, typeId, offset);
        }
        if (slot != 0 && Assignable(arena, arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotType], targetType) == 0)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidCast, slot, targetType);
        }
        uint payload = arena[type + WarpPortableHeapLayout.StaticStart] + offset;
        arena[payload] = context;
        arena[payload + 1] = slot;
        arena[payload + 2] = generation;
        return 0;
    }

    private static uint RequireInitializerOwner(uint[] arena, uint typeId, uint worker)
    {
        if (RequireType(arena, typeId) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint type = Type(arena, typeId);
        return arena[type + WarpPortableHeapLayout.InitializerState] == 1 && arena[type + WarpPortableHeapLayout.InitializerOwner] == worker ? 0 :
            Fail(arena, WarpPortableHeapLayout.InvalidOperation, typeId, worker);
    }
}
