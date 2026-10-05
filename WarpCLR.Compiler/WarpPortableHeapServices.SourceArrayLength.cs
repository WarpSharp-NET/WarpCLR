namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    // The admitted source length bound controls the value, never the CLI evaluation width.
    // Native unsigned CLI64 results use low/high words even when the high word is zero.
    public static uint ReadSourceArrayLength(uint[] arena, uint context, uint slot, uint generation)
    {
        if (Begin(arena, 54) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        if (SourceArrayShape(arena, context, slot, generation) == 0) { return arena[WarpPortableHeapLayout.Fault]; }
        arena[WarpPortableHeapLayout.Result] = arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotLength];
        arena[WarpPortableHeapLayout.Result + 1] = 0;
        return 0;
    }
}
