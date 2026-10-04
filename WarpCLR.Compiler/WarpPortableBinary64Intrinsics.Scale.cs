namespace WarpCLR.Compiler;

internal static partial class WarpPortableBinary64Intrinsics
{
    public static uint ScaleBLow(uint low, uint high, uint scale) => ScaleB(low, high, scale, 0);

    public static uint ScaleBHigh(uint low, uint high, uint scale) => ScaleB(low, high, scale, 1);

    private static uint ScaleB(uint low, uint high, uint scale, uint word)
    {
        uint exponent = high >> 20 & 0x7FFu;
        if (IsNaN(low, high) != 0)
        {
            return word == 0 ? 0 : 0x7FF80000u;
        }

        if (exponent == 0x7FFu || ((high & 0x7FFFFFFFu) | low) == 0)
        {
            return word == 0 ? low : high;
        }

        uint sign = high >> 31;
        uint negativeScale = scale >> 31;
        uint distance = negativeScale != 0 ? unchecked(0u - scale) : scale;
        if (distance > 4096)
        {
            return word == 0 ? 0 : sign << 31 | (negativeScale == 0 ? 0x7FF00000u : 0);
        }

        high = (high & 0xFFFFFu) | (exponent == 0 ? 0 : 0x100000u);
        exponent = (exponent == 0 ? 1 : exponent) + 4096;
        while ((high & 0x100000u) == 0)
        {
            high = high << 1 | low >> 31;
            low <<= 1;
            exponent--;
        }

        if (negativeScale != 0 && distance >= exponent)
        {
            return word == 0 ? 0 : sign << 31;
        }

        exponent = negativeScale == 0 ? exponent + distance : exponent - distance;
        return ScaleNormalized(low, high, exponent, sign, word);
    }

    private static uint ScaleNormalized(uint low, uint high, uint exponent, uint sign, uint word)
    {
        if (exponent > 4096)
        {
            exponent -= 4096;
            return exponent >= 0x7FFu ? (word == 0 ? 0 : sign << 31 | 0x7FF00000u) :
                word == 0 ? low : sign << 31 | exponent << 20 | high & 0xFFFFFu;
        }

        uint distance = 4097 - exponent;
        high = high << 3 | low >> 29;
        low <<= 3;
        low = ShiftPairJamLow(low, high, distance);
        high = distance < 32 ? high >> (int)distance : 0;
        uint roundBits = low & 7u;
        low = low >> 3 | high << 29;
        high >>= 3;
        if (roundBits > 4 || (roundBits == 4 && (low & 1u) != 0))
        {
            low = unchecked(low + 1);
            high += low == 0 ? 1u : 0u;
        }

        return word == 0 ? low : sign << 31 | (high & 0x100000u) | high & 0xFFFFFu;
    }

    private static uint ShiftPairJamLow(uint low, uint high, uint distance)
    {
        if (distance < 32)
        {
            return low >> (int)distance | high << (int)(32 - distance) | ((low << (int)(32 - distance)) != 0 ? 1u : 0u);
        }

        if (distance == 32)
        {
            return high | (low != 0 ? 1u : 0u);
        }

        return distance < 64 ? high >> (int)(distance - 32) | (((high << (int)(64 - distance)) | low) != 0 ? 1u : 0u) :
            (low | high) != 0 ? 1u : 0u;
    }
}
