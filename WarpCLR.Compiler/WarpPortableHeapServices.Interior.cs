namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    public static uint MakeInterior(uint[] arena, uint context, uint slot, uint generation, uint offset, uint words, uint typeId)
    {
        if (Begin(arena, 30) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequireInterior(arena, context, slot, generation, offset, words, typeId) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        SetReferenceResult(arena, context, slot, generation);
        arena[WarpPortableHeapLayout.Result + 3] = offset;
        arena[WarpPortableHeapLayout.Result + 4] = words;
        arena[WarpPortableHeapLayout.Result + 5] = typeId;
        return 0;
    }

    private static uint RequireInterior(uint[] arena, uint context, uint slot, uint generation, uint offset, uint words, uint typeId)
    {
        if (RequireType(arena, typeId) != 0 || RequirePayload(arena, context, slot, generation, offset, words) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint type = Type(arena, typeId);
        if (arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotKind] == WarpPortableHeapLayout.String || words == 0)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, slot, words);
        }
        if (arena[type + WarpPortableHeapLayout.TypeKind] != WarpPortableHeapLayout.Value)
        {
            return words == 3 && ReferenceTypeAt(arena, slot, offset, 0) == typeId ? 0 :
                Fail(arena, WarpPortableHeapLayout.InvalidOperation, slot, typeId);
        }
        if (words != arena[type + WarpPortableHeapLayout.TypePayloadWords])
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, words, typeId);
        }
        for (uint word = 0; word < words; word++)
        {
            if (ReferenceTypeAt(arena, slot, offset + word, 1) != ValueReferenceTypeAt(arena, type, word))
            {
                return Fail(arena, WarpPortableHeapLayout.InvalidOperation, offset + word, typeId);
            }
        }
        return 0;
    }

    private static uint ValueReferenceTypeAt(uint[] arena, uint type, uint offset)
    {
        uint map = arena[type + WarpPortableHeapLayout.ReferenceMap];
        uint count = arena[type + WarpPortableHeapLayout.ReferenceCount];
        for (uint index = 0; index < count; index++)
        {
            uint start = arena[map + index * 2];
            if (offset >= start && offset - start < 3)
            {
                return arena[map + index * 2 + 1];
            }
        }
        return 0;
    }
}
