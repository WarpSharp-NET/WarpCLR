namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    public static uint BoxValue(uint[] arena, uint typeId, uint offset)
    {
        if (Begin(arena, 11) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequireType(arena, typeId) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint type = Type(arena, typeId);
        uint words = arena[type + WarpPortableHeapLayout.TypePayloadWords];
        if (arena[type + WarpPortableHeapLayout.TypeKind] != WarpPortableHeapLayout.Value)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidType, typeId, 0);
        }
        if (RequireScratch(arena, offset, words) != 0)
        {
            return Fail(arena, WarpPortableHeapLayout.Bounds, offset, words);
        }
        if (ValidateValueReferences(arena, type, offset) != 0 || Allocate(arena, typeId, WarpPortableHeapLayout.Box, 0) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint payload = arena[Slot(arena, arena[WarpPortableHeapLayout.Result + 1]) + WarpPortableHeapLayout.SlotPayload];
        for (uint word = 0; word < words; word++)
        {
            arena[payload + word] = arena[arena[WarpPortableHeapLayout.ScratchStart] + offset + word];
        }
        return 0;
    }

    public static uint UnboxValue(uint[] arena, uint context, uint slot, uint generation, uint typeId, uint offset)
    {
        if (Begin(arena, 12) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequireReference(arena, context, slot, generation, 0) != 0 || RequireType(arena, typeId) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint entry = Slot(arena, slot);
        if (arena[entry + WarpPortableHeapLayout.SlotKind] != WarpPortableHeapLayout.Box || arena[entry + WarpPortableHeapLayout.SlotType] != typeId)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidCast, slot, typeId);
        }
        uint words = arena[entry + WarpPortableHeapLayout.SlotPayloadWords];
        if (RequireScratch(arena, offset, words) != 0)
        {
            return Fail(arena, WarpPortableHeapLayout.Bounds, offset, words);
        }
        uint payload = arena[entry + WarpPortableHeapLayout.SlotPayload];
        for (uint word = 0; word < words; word++)
        {
            arena[arena[WarpPortableHeapLayout.ScratchStart] + offset + word] = arena[payload + word];
        }
        return 0;
    }

    public static uint WriteValueArrayElement(uint[] arena, uint context, uint slot, uint generation, uint index, uint offset)
    {
        if (Begin(arena, 13) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequireReference(arena, context, slot, generation, 0) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint entry = Slot(arena, slot);
        uint type = Type(arena, arena[entry + WarpPortableHeapLayout.SlotType]);
        uint words = arena[type + WarpPortableHeapLayout.ElementWords];
        if (arena[entry + WarpPortableHeapLayout.SlotKind] != WarpPortableHeapLayout.ValueArray)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, slot, 0);
        }
        if (index >= arena[entry + WarpPortableHeapLayout.SlotLength])
        {
            return Fail(arena, WarpPortableHeapLayout.Bounds, index, offset);
        }
        if (RequireScratch(arena, offset, words) != 0 || ValidateValueReferences(arena, Type(arena, arena[type + WarpPortableHeapLayout.ElementType]), offset) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint payload = arena[entry + WarpPortableHeapLayout.SlotPayload] + index * words;
        for (uint word = 0; word < words; word++)
        {
            arena[payload + word] = arena[arena[WarpPortableHeapLayout.ScratchStart] + offset + word];
        }
        return 0;
    }

    private static uint ValidateValueReferences(uint[] arena, uint type, uint offset)
    {
        uint count = arena[type + WarpPortableHeapLayout.ReferenceCount];
        uint map = arena[type + WarpPortableHeapLayout.ReferenceMap];
        for (uint index = 0; index < count; index++)
        {
            uint start = arena[WarpPortableHeapLayout.ScratchStart] + offset + arena[map + index * 2];
            if (RequireReference(arena, arena[start], arena[start + 1], arena[start + 2], 1) != 0)
            {
                return arena[WarpPortableHeapLayout.Fault];
            }
            if (arena[start + 1] != 0 && Assignable(arena, arena[Slot(arena, arena[start + 1]) + WarpPortableHeapLayout.SlotType], arena[map + index * 2 + 1]) == 0)
            {
                return Fail(arena, WarpPortableHeapLayout.InvalidCast, arena[start + 1], arena[map + index * 2 + 1]);
            }
        }
        return 0;
    }

    private static uint RequireScratch(uint[] arena, uint offset, uint words) =>
        offset <= arena[WarpPortableHeapLayout.ScratchWords] && words <= arena[WarpPortableHeapLayout.ScratchWords] - offset ? 0 :
            Fail(arena, WarpPortableHeapLayout.Bounds, offset, words);
}
