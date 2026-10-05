namespace WarpCLR.Compiler;

internal static partial class WarpPortablePrimitiveFormatServices
{
    private static uint IntegerDigits(uint[] arena, uint kind, uint low, uint high, uint scratch)
    {
        uint sign = 0;
        if (kind <= WarpPortablePrimitiveFormatLayout.UInt16)
        {
            uint mask = kind < WarpPortablePrimitiveFormatLayout.Int16 ? 0xFFU : 0xFFFFU;
            low &= mask;
            if ((kind & 1) == 0 && (low & ((mask >> 1) + 1)) != 0)
            {
                sign = 1;
                low = unchecked(0U - low) & mask;
            }
        }
        else if (kind == WarpPortablePrimitiveFormatLayout.Int32 && (low >> 31) != 0)
        {
            sign = 1;
            low = unchecked(0U - low);
        }
        else if (kind == WarpPortablePrimitiveFormatLayout.Int64 && (high >> 31) != 0)
        {
            sign = 1;
            low = unchecked(0U - low);
            high = unchecked(~high + (low == 0 ? 1U : 0U));
        }
        arena[scratch + 1] = sign;
        uint count = 0;
        do
        {
            arena[scratch + 13] = low;
            arena[scratch + 14] = high;
            uint remainder = DividePairByTen(arena, scratch);
            low = arena[scratch + 13];
            high = arena[scratch + 14];
            arena[scratch + WarpPortablePrimitiveFormatLayout.Digits + count] = remainder;
            count++;
        }
        while ((low | high) != 0);
        arena[scratch + 11] = count;
        return 0;
    }

    private static uint DividePairByTen(uint[] arena, uint scratch)
    {
        uint low = arena[scratch + 13];
        uint high = arena[scratch + 14];
        uint quotientLow = 0;
        uint quotientHigh = 0;
        uint remainder = 0;
        for (uint remaining = 64; remaining != 0; remaining--)
        {
            uint bit = remaining - 1;
            uint next = bit >= 32 ? (high >> (int)(bit - 32)) & 1 : (low >> (int)bit) & 1;
            remainder = remainder * 2 + next;
            if (remainder >= 10)
            {
                remainder -= 10;
                if (bit >= 32) { quotientHigh |= 1U << (int)(bit - 32); }
                else { quotientLow |= 1U << (int)bit; }
            }
        }
        arena[scratch + 13] = quotientLow;
        arena[scratch + 14] = quotientHigh;
        return remainder;
    }
}
