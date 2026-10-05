namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    public static uint ValidateSourceFrameView(uint[] arena, uint function, uint privateWords,
        uint byteOffset, uint byteSpan, uint elementType)
    {
        uint descriptor = SourceViewDescriptor(arena);
        if (descriptor == 0 || RequireType(arena, elementType) != 0) { return Fail(arena, WarpPortableHeapLayout.InvalidType, elementType, descriptor); }
        uint start = arena[descriptor + WarpPortableSourceMemoryLayout.FrameStart];
        uint count = arena[descriptor + WarpPortableSourceMemoryLayout.FrameCount];
        for (uint index = 0; index < count; index++)
        {
            uint row = start + index * WarpPortableSourceMemoryLayout.FrameWords;
            if (arena[row + WarpPortableSourceMemoryLayout.FrameFunction] != function) { continue; }
            if (arena[row + WarpPortableSourceMemoryLayout.FramePrivateWords] != privateWords || privateWords > 131072 ||
                byteSpan == 0 || byteOffset > privateWords * 4 || byteSpan > privateWords * 4 - byteOffset)
            {
                return Fail(arena, WarpPortableHeapLayout.Bounds, byteOffset, byteSpan);
            }
            return SourceFrameViewMatches(arena, descriptor, row, byteOffset, byteSpan, elementType) != 0 ? 0 :
                Fail(arena, WarpPortableHeapLayout.InvalidCast, function, elementType);
        }
        return Fail(arena, WarpPortableHeapLayout.InvalidOperation, function, privateWords);
    }

    private static uint SourceFrameViewMatches(uint[] arena, uint descriptor, uint frame, uint byteOffset, uint byteSpan, uint elementType)
    {
        uint start = arena[frame + WarpPortableSourceMemoryLayout.FrameViews];
        uint count = arena[frame + WarpPortableSourceMemoryLayout.FrameViewsCount];
        uint minimum = arena[descriptor + WarpPortableSourceMemoryLayout.FrameViewStart];
        uint maximum = minimum + arena[descriptor + WarpPortableSourceMemoryLayout.FrameViewCount] * WarpPortableSourceMemoryLayout.FrameViewWords;
        if (count > 349525 || start < minimum || start > maximum || count * WarpPortableSourceMemoryLayout.FrameViewWords > maximum - start) { return 0; }
        for (uint index = 0; index < count; index++)
        {
            uint row = start + index * WarpPortableSourceMemoryLayout.FrameViewWords;
            if (arena[row] == byteOffset && arena[row + 1] == byteSpan && arena[row + 2] == elementType) { return 1; }
        }
        return 0;
    }
}
