namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    public static uint ReadStaticWord(uint[] arena, uint typeId, uint offset)
    {
        if (Begin(arena, 31) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequireStaticWord(arena, typeId, offset) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        arena[WarpPortableHeapLayout.Result] = arena[arena[Type(arena, typeId) + WarpPortableHeapLayout.StaticStart] + offset];
        return 0;
    }

    public static uint WriteStaticWord(uint[] arena, uint typeId, uint offset, uint value)
    {
        if (Begin(arena, 32) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequireStaticWord(arena, typeId, offset) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        arena[arena[Type(arena, typeId) + WarpPortableHeapLayout.StaticStart] + offset] = value;
        return 0;
    }

    private static uint RequireStaticWord(uint[] arena, uint typeId, uint offset)
    {
        if (RequireType(arena, typeId) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint type = Type(arena, typeId);
        if (offset >= arena[type + WarpPortableHeapLayout.StaticWords])
        {
            return Fail(arena, WarpPortableHeapLayout.Bounds, offset, arena[type + WarpPortableHeapLayout.StaticWords]);
        }
        uint map = arena[type + WarpPortableHeapLayout.StaticReferenceMap];
        for (uint index = 0; index < arena[type + WarpPortableHeapLayout.StaticReferenceCount]; index++)
        {
            uint start = arena[map + index * 2];
            if (offset >= start && offset - start < 3)
            {
                return Fail(arena, WarpPortableHeapLayout.InvalidOperation, typeId, offset);
            }
        }
        return 0;
    }
}
