namespace WarpCLR.Compiler;

internal static partial class WarpPortablePrimitiveFormatServices
{
    private static uint InRange(uint[] arena, uint offset, uint count) =>
        offset <= (uint)arena.Length && count <= (uint)arena.Length - offset ? 1U : 0U;

    private static uint Disjoint(uint left, uint leftCount, uint right, uint rightCount) =>
        left <= right ? leftCount <= right - left ? 1U : 0U : rightCount <= left - right ? 1U : 0U;

    private static uint Validate(uint[] arena, uint contract, uint scratch, uint output, uint capacity)
    {
        if (InRange(arena, contract, WarpPortablePrimitiveFormatLayout.ContractHeader) == 0 ||
            InRange(arena, scratch, WarpPortablePrimitiveFormatLayout.ScratchWords) == 0 || InRange(arena, output, capacity) == 0)
        { return WarpPortablePrimitiveFormatLayout.BadShape; }
        uint total = arena[contract + 1];
        if (arena[contract] != WarpPortablePrimitiveFormatLayout.Magic || arena[contract + 2] != 1 || arena[contract + 23] != 0 ||
            total < WarpPortablePrimitiveFormatLayout.ContractHeader || InRange(arena, contract, total) == 0)
        { return WarpPortablePrimitiveFormatLayout.BadShape; }
        if (Disjoint(contract, total, scratch, WarpPortablePrimitiveFormatLayout.ScratchWords) == 0 ||
            Disjoint(contract, total, output, capacity) == 0 || Disjoint(scratch, WarpPortablePrimitiveFormatLayout.ScratchWords, output, capacity) == 0)
        { return WarpPortablePrimitiveFormatLayout.BadShape; }
        uint cursor = WarpPortablePrimitiveFormatLayout.ContractHeader;
        for (uint symbol = 0; symbol < 6; symbol++)
        {
            uint offset = arena[contract + 3 + symbol * 2];
            uint length = arena[contract + 4 + symbol * 2];
            if (offset != cursor || length > WarpPortablePrimitiveFormatLayout.SymbolLimit || length > total - cursor)
            { return WarpPortablePrimitiveFormatLayout.BadShape; }
            for (uint word = 0; word < length; word++)
            {
                if (arena[contract + cursor + word] > 0xFFFF) { return WarpPortablePrimitiveFormatLayout.BadShape; }
            }
            cursor += length;
        }
        return cursor == total ? 0U : WarpPortablePrimitiveFormatLayout.BadShape;
    }
}
