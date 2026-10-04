namespace WarpCLR.Compiler;

internal static partial class WarpPortableBinary64Transcendentals
{
    private static uint ProductWord(uint low, uint high, uint index)
    {
        uint carryLow = 0;
        uint carryHigh = 0;
        uint result = 0;
        for (uint digit = 0; digit <= index; digit++)
        {
            uint first = TwoOverPi(digit);
            uint second = digit == 0 ? 0 : TwoOverPi(digit - 1);
            uint firstLow = unchecked(low * first);
            uint secondLow = unchecked(high * second);
            result = unchecked(firstLow + carryLow);
            uint overflow = result < firstLow ? 1u : 0u;
            uint nextResult = unchecked(result + secondLow);
            overflow += nextResult < result ? 1u : 0u;
            result = nextResult;
            uint nextLow = MultiplyWordHigh(low, first);
            uint nextHigh = 0;
            uint sum = unchecked(nextLow + MultiplyWordHigh(high, second));
            nextHigh += sum < nextLow ? 1u : 0u;
            nextLow = sum;
            sum = unchecked(nextLow + carryHigh);
            nextHigh += sum < nextLow ? 1u : 0u;
            nextLow = sum;
            sum = unchecked(nextLow + overflow);
            nextHigh += sum < nextLow ? 1u : 0u;
            carryLow = sum;
            carryHigh = nextHigh;
        }

        return result;
    }

    private static uint ProductBits(uint low, uint high, uint position)
    {
        uint distance = position & 31u;
        uint result = ProductWord(low, high, position >> 5);
        return distance == 0 ? result : result >> (int)distance | ProductWord(low, high, (position >> 5) + 1) << (int)(32 - distance);
    }

    private static uint Reduction(uint low, uint high, uint word)
    {
        uint exponent = high >> 20;
        high = (high & 0xFFFFFu) | 0x100000u;
        uint point = 3123 - exponent;
        uint fraction5 = ProductBits(low, high, point - 32);
        uint roundedUp = fraction5 >> 31;
        uint quadrant = unchecked((ProductBits(low, high, point) + roundedUp) & 3u);
        if (word == 2) { return quadrant; }
        return PhaseToRadians(ProductBits(low, high, point - 192), ProductBits(low, high, point - 160),
            ProductBits(low, high, point - 128), ProductBits(low, high, point - 96),
            ProductBits(low, high, point - 64), fraction5, roundedUp, word);
    }

    private static uint PhaseToRadians(uint digit0, uint digit1, uint digit2, uint digit3, uint digit4,
        uint digit5, uint sign, uint word)
    {
        if (sign != 0)
        {
            digit0 = unchecked(0u - digit0);
            digit1 = unchecked(~digit1 + (digit0 == 0 ? 1u : 0u));
            digit2 = unchecked(~digit2 + ((digit0 | digit1) == 0 ? 1u : 0u));
            digit3 = unchecked(~digit3 + ((digit0 | digit1 | digit2) == 0 ? 1u : 0u));
            digit4 = unchecked(~digit4 + ((digit0 | digit1 | digit2 | digit3) == 0 ? 1u : 0u));
            digit5 = unchecked(~digit5 + ((digit0 | digit1 | digit2 | digit3 | digit4) == 0 ? 1u : 0u));
        }

        uint exponent = 1022;
        while ((digit5 & 0x80000000u) == 0)
        {
            digit5 = digit5 << 1 | digit4 >> 31;
            digit4 = digit4 << 1 | digit3 >> 31;
            digit3 = digit3 << 1 | digit2 >> 31;
            digit2 = digit2 << 1 | digit1 >> 31;
            digit1 = digit1 << 1 | digit0 >> 31;
            digit0 <<= 1;
            exponent--;
        }

        uint phaseLow = digit5 << 21 | digit4 >> 11;
        uint phaseHigh = digit5 >> 11;
        uint trailing = digit4 & 0x7FFu;
        if (trailing > 0x400u || (trailing == 0x400u && ((digit3 | digit2 | digit1 | digit0) != 0 || (phaseLow & 1u) != 0)))
        {
            phaseLow = unchecked(phaseLow + 1);
            phaseHigh += phaseLow == 0 ? 1u : 0u;
        }

        if ((phaseHigh & 0x200000u) != 0) { phaseHigh >>= 1; phaseLow >>= 1; exponent++; }
        phaseHigh = sign << 31 | exponent << 20 | phaseHigh & 0xFFFFFu;
        return Multiply(phaseLow, phaseHigh, ConstantLow(2), ConstantHigh(2), word);
    }
}
