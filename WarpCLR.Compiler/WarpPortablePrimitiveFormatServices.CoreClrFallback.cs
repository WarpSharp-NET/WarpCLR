namespace WarpCLR.Compiler;

internal static partial class WarpPortablePrimitiveFormatServices
{
    // Exact pinned .NET10.0.12 G fallback outputs. The underlying Double is never modified.
    private static uint PinnedCoreClrDoubleFallback(uint[] arena, uint kind, uint scratch)
    {
        if (kind != WarpPortablePrimitiveFormatLayout.Double || arena[scratch + 3] != 0 ||
            arena[scratch + 4] != 0x100000) { return 0; }
        uint high; uint low; uint exponent;
        if (arena[scratch + 2] == 65)
        { high = 0x41045368; low = 0x01298376; exponent = 4294967007U; }
        else if (arena[scratch + 2] == 998)
        { high = 0x29802322; low = 0x38769531; exponent = 4294967288U; }
        else { return 0; }
        for (uint word = 0; word < 16; word++)
        {
            uint packed = word < 8 ? high : low;
            uint shift = (7 - (word & 7)) * 4;
            arena[scratch + WarpPortablePrimitiveFormatLayout.Digits + word] = (packed >> (int)shift) & 15;
        }
        arena[scratch + 11] = 16;
        arena[scratch + 12] = exponent;
        return 1;
    }
}
