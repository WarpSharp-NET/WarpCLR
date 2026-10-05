namespace WarpCLR.Compiler;

internal static partial class WarpPortablePrimitiveFormatServices
{
    private static uint Decode(uint[] arena, uint kind, uint low, uint high, uint scratch)
    {
        uint exponent;
        uint fractionLow;
        uint fractionHigh;
        uint maximum;
        if (kind == WarpPortablePrimitiveFormatLayout.Single)
        {
            arena[scratch + 1] = low >> 31;
            exponent = (low >> 23) & 0xFF;
            fractionLow = low & 0x7FFFFF;
            fractionHigh = 0;
            maximum = 0xFF;
            arena[scratch + 5] = 150;
            arena[scratch + 9] = 9;
        }
        else
        {
            arena[scratch + 1] = high >> 31;
            exponent = (high >> 20) & 0x7FF;
            fractionLow = low;
            fractionHigh = high & 0xFFFFF;
            maximum = 0x7FF;
            arena[scratch + 5] = 1075;
            arena[scratch + 9] = 17;
        }
        if (exponent == maximum)
        {
            if ((fractionLow | fractionHigh) != 0) { return 3; }
            return arena[scratch + 1] == 0 ? 4U : 5U;
        }
        if ((exponent | fractionLow | fractionHigh) == 0) { return 1; }
        arena[scratch + 2] = exponent;
        arena[scratch + 3] = fractionLow;
        arena[scratch + 4] = fractionHigh;
        if (exponent != 0)
        {
            if (kind == WarpPortablePrimitiveFormatLayout.Single) { arena[scratch + 3] |= 0x800000; }
            else { arena[scratch + 4] |= 0x100000; }
        }
        return 2;
    }

    private static uint InitializeFloat(uint[] arena, uint kind, uint scratch)
    {
        uint exponent = arena[scratch + 2];
        uint low = arena[scratch + 3];
        uint high = arena[scratch + 4];
        uint unequal = exponent > 1 && (kind == WarpPortablePrimitiveFormatLayout.Single
            ? low == 0x800000 : low == 0 && high == 0x100000) ? 1U : 0U;
        uint factor = unequal == 0 ? 2U : 4U;
        Set(arena, scratch + WarpPortablePrimitiveFormatLayout.Value, low, high);
        Multiply(arena, scratch + WarpPortablePrimitiveFormatLayout.Value, factor);
        Set(arena, scratch + WarpPortablePrimitiveFormatLayout.Scale, factor, 0);
        Set(arena, scratch + WarpPortablePrimitiveFormatLayout.MarginLow, 1, 0);
        Set(arena, scratch + WarpPortablePrimitiveFormatLayout.MarginHigh, unequal == 0 ? 1U : 2U, 0);
        uint field = exponent == 0 ? 1U : exponent;
        uint threshold = arena[scratch + 5];
        if (field < threshold) { return Shift(arena, scratch + WarpPortablePrimitiveFormatLayout.Scale, threshold - field); }
        uint shift = field - threshold;
        return Shift(arena, scratch + WarpPortablePrimitiveFormatLayout.Value, shift) |
            Shift(arena, scratch + WarpPortablePrimitiveFormatLayout.MarginLow, shift) |
            Shift(arena, scratch + WarpPortablePrimitiveFormatLayout.MarginHigh, shift);
    }

    private static uint NormalizeFloat(uint[] arena, uint scratch)
    {
        uint value = scratch + WarpPortablePrimitiveFormatLayout.Value;
        uint scale = scratch + WarpPortablePrimitiveFormatLayout.Scale;
        uint temporary = scratch + WarpPortablePrimitiveFormatLayout.Temporary;
        uint exponent = 0;
        if (Compare(arena, value, scale) == 0)
        {
            do
            {
                if ((Multiply(arena, value, 10) | Multiply(arena, scratch + WarpPortablePrimitiveFormatLayout.MarginLow, 10) |
                    Multiply(arena, scratch + WarpPortablePrimitiveFormatLayout.MarginHigh, 10)) != 0)
                { return WarpPortablePrimitiveFormatLayout.ArithmeticCapacity; }
                exponent = unchecked(exponent - 1);
            }
            while (Compare(arena, value, scale) == 0);
        }
        else
        {
            Copy(arena, temporary, scale);
            if (Multiply(arena, temporary, 10) != 0) { return WarpPortablePrimitiveFormatLayout.ArithmeticCapacity; }
            while (Compare(arena, value, temporary) != 0)
            {
                Copy(arena, scale, temporary);
                if (Multiply(arena, temporary, 10) != 0) { return WarpPortablePrimitiveFormatLayout.ArithmeticCapacity; }
                exponent++;
            }
        }
        arena[scratch + 12] = exponent;
        return 0;
    }

    private static uint GenerateFloatDigits(uint[] arena, uint scratch)
    {
        uint value = scratch + WarpPortablePrimitiveFormatLayout.Value;
        uint scale = scratch + WarpPortablePrimitiveFormatLayout.Scale;
        uint temporary = scratch + WarpPortablePrimitiveFormatLayout.Temporary;
        uint even = (arena[scratch + 3] & 1) == 0 ? 1U : 0U;
        for (uint count = 0; count < 20; count++)
        {
            uint digit = Digit(arena, value, scale);
            uint lowCompare = Compare(arena, value, scratch + WarpPortablePrimitiveFormatLayout.MarginLow);
            uint lower = lowCompare == 0 || lowCompare == 1 && even != 0 ? 1U : 0U;
            if (Add(arena, temporary, value, scratch + WarpPortablePrimitiveFormatLayout.MarginHigh) != 0)
            { return WarpPortablePrimitiveFormatLayout.ArithmeticCapacity; }
            uint highCompare = Compare(arena, temporary, scale);
            uint upper = highCompare == 2 || highCompare == 1 && even != 0 ? 1U : 0U;
            if ((lower | upper) != 0)
            {
                if (ShouldRoundUp(arena, scratch, lower, upper, digit) != 0) { digit++; }
                arena[scratch + WarpPortablePrimitiveFormatLayout.Digits + count] = digit;
                arena[scratch + 11] = count + 1;
                RoundDigits(arena, scratch);
                return 0;
            }
            arena[scratch + WarpPortablePrimitiveFormatLayout.Digits + count] = digit;
            if ((Multiply(arena, value, 10) | Multiply(arena, scratch + WarpPortablePrimitiveFormatLayout.MarginLow, 10) |
                Multiply(arena, scratch + WarpPortablePrimitiveFormatLayout.MarginHigh, 10)) != 0)
            { return WarpPortablePrimitiveFormatLayout.ArithmeticCapacity; }
        }
        return WarpPortablePrimitiveFormatLayout.ArithmeticCapacity;
    }

    private static uint ShouldRoundUp(uint[] arena, uint scratch, uint lower, uint upper, uint digit)
    {
        if (lower == 0) { return 1; }
        if (upper == 0) { return 0; }
        uint doubled = scratch + WarpPortablePrimitiveFormatLayout.Doubled;
        Copy(arena, doubled, scratch + WarpPortablePrimitiveFormatLayout.Value);
        Multiply(arena, doubled, 2);
        uint comparison = Compare(arena, doubled, scratch + WarpPortablePrimitiveFormatLayout.Scale);
        return comparison == 2 || comparison == 1 && (digit & 1) != 0 ? 1U : 0U;
    }

    private static uint RoundDigits(uint[] arena, uint scratch)
    {
        uint digits = scratch + WarpPortablePrimitiveFormatLayout.Digits;
        uint count = arena[scratch + 11];
        uint cursor = count;
        while (cursor != 0 && arena[digits + cursor - 1] == 10)
        {
            arena[digits + cursor - 1] = 0;
            cursor--;
            if (cursor == 0) { arena[digits] = 1; count = 1; arena[scratch + 12]++; }
            else { arena[digits + cursor - 1]++; }
        }
        while (count > 1 && arena[digits + count - 1] == 0) { count--; }
        arena[scratch + 11] = count;
        return 0;
    }
}
