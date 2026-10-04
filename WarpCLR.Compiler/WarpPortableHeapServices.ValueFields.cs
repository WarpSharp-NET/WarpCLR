namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    public static uint ReadValue(uint[] arena, uint context, uint slot, uint generation, uint fieldOffset, uint typeId, uint offset)
    {
        if (Begin(arena, 33) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequireType(arena, typeId) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint words = arena[Type(arena, typeId) + WarpPortableHeapLayout.TypePayloadWords];
        if (RequireInterior(arena, context, slot, generation, fieldOffset, words, typeId) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequireScratch(arena, offset, words) != 0)
        {
            return Fail(arena, WarpPortableHeapLayout.Bounds, offset, words);
        }
        uint payload = arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotPayload] + fieldOffset;
        for (uint word = 0; word < words; word++)
        {
            arena[arena[WarpPortableHeapLayout.ScratchStart] + offset + word] = arena[payload + word];
        }
        return 0;
    }

    public static uint WriteValue(uint[] arena, uint context, uint slot, uint generation, uint fieldOffset, uint typeId, uint offset)
    {
        if (Begin(arena, 34) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequireType(arena, typeId) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint type = Type(arena, typeId);
        uint words = arena[type + WarpPortableHeapLayout.TypePayloadWords];
        if (RequireInterior(arena, context, slot, generation, fieldOffset, words, typeId) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequireScratch(arena, offset, words) != 0)
        {
            return Fail(arena, WarpPortableHeapLayout.Bounds, offset, words);
        }
        if (ValidateValueReferences(arena, type, offset) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint payload = arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotPayload] + fieldOffset;
        for (uint word = 0; word < words; word++)
        {
            arena[payload + word] = arena[arena[WarpPortableHeapLayout.ScratchStart] + offset + word];
        }
        return 0;
    }
}
