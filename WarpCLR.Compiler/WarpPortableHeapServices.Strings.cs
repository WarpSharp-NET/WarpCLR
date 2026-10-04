namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    public static uint CreateString(uint[] arena, uint typeId, uint offset, uint length)
    {
        if (Begin(arena, 14) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequireType(arena, typeId) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (arena[Type(arena, typeId) + WarpPortableHeapLayout.TypeKind] != WarpPortableHeapLayout.String)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidType, typeId, 0);
        }
        if (RequireScratch(arena, offset, length) != 0)
        {
            return Fail(arena, WarpPortableHeapLayout.Bounds, offset, length);
        }
        for (uint index = 0; index < length; index++)
        {
            if (arena[arena[WarpPortableHeapLayout.ScratchStart] + offset + index] > 0xFFFFu)
            {
                return Fail(arena, WarpPortableHeapLayout.InvalidOperation, arena[arena[WarpPortableHeapLayout.ScratchStart] + offset + index], index);
            }
        }
        if (Allocate(arena, typeId, WarpPortableHeapLayout.String, length) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint payload = arena[Slot(arena, arena[WarpPortableHeapLayout.Result + 1]) + WarpPortableHeapLayout.SlotPayload];
        for (uint index = 0; index < length; index++)
        {
            arena[payload + (index >> 1)] |= arena[arena[WarpPortableHeapLayout.ScratchStart] + offset + index] << (int)((index & 1u) * 16);
        }
        return 0;
    }

    public static uint StringCharacter(uint[] arena, uint context, uint slot, uint generation, uint index)
    {
        if (Begin(arena, 15) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequireReference(arena, context, slot, generation, 0) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint entry = Slot(arena, slot);
        if (arena[entry + WarpPortableHeapLayout.SlotKind] != WarpPortableHeapLayout.String)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, slot, 0);
        }
        if (index >= arena[entry + WarpPortableHeapLayout.SlotLength])
        {
            return Fail(arena, WarpPortableHeapLayout.Bounds, index, arena[entry + WarpPortableHeapLayout.SlotLength]);
        }
        uint payload = arena[entry + WarpPortableHeapLayout.SlotPayload];
        arena[WarpPortableHeapLayout.Result] = arena[payload + (index >> 1)] >> (int)((index & 1u) * 16) & 0xFFFFu;
        return 0;
    }
}
