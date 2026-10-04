namespace WarpCLR.Compiler;

internal static partial class WarpPortableBinary64Math
{
    internal const string Semantics = "warp.math.binary64.sqrt-rem-ieeerem-fma/rne-gradual-single-round-canonical-nan/0.1";
    private const uint ExponentOffset = 8192;

    public static uint SqrtLow(uint low, uint high) => Sqrt(low, high, 64, 0);

    public static uint SqrtHigh(uint low, uint high) => Sqrt(low, high, 64, 1);

    public static uint RemainderLow(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        Remainder(leftLow, leftHigh, rightLow, rightHigh, 0, 64, 0);

    public static uint RemainderHigh(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        Remainder(leftLow, leftHigh, rightLow, rightHigh, 0, 64, 1);

    public static uint IeeeRemainderLow(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        Remainder(leftLow, leftHigh, rightLow, rightHigh, 1, 64, 0);

    public static uint IeeeRemainderHigh(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        Remainder(leftLow, leftHigh, rightLow, rightHigh, 1, 64, 1);

    internal static uint SqrtSingle(uint value) =>
        Sqrt(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value), 32, 0);

    internal static uint RemainderSingle(uint left, uint right, uint nearest) =>
        Remainder(WarpPortableNumericConversions.SingleToDoubleLow(left), WarpPortableNumericConversions.SingleToDoubleHigh(left),
            WarpPortableNumericConversions.SingleToDoubleLow(right), WarpPortableNumericConversions.SingleToDoubleHigh(right), nearest, 32, 0);

    private static uint Sqrt(uint low, uint high, uint precision, uint word)
    {
        if (IsNaN(low, high) != 0 || ((high >> 31) != 0 && ((high & 0x7FFFFFFFu) | low) != 0))
        {
            return Special(2, 0, precision, word);
        }

        if (IsInfinite(high) != 0 || ((high & 0x7FFFFFFFu) | low) == 0)
        {
            return Transport(low, high, precision, word);
        }

        return SqrtFinite(Normalize(low, high, 0), Normalize(low, high, 1), Normalize(low, high, 2), precision, word);
    }

    private static uint SqrtFinite(uint low, uint high, uint exponent, uint precision, uint word)
    {
        uint unbiased = exponent - 1023;
        uint shift = 58 + (unbiased & 1u);
        uint remainderLow = 0;
        uint remainderHigh = 0;
        uint rootLow = 0;
        uint rootHigh = 0;
        for (uint digit = 0; digit < 56; digit++)
        {
            uint position = 110 - 2 * digit;
            uint pair = position + 1 < shift ? 0 : position < shift ? (low & 1u) << 1 : PairBits(low, high, position - shift);
            remainderHigh = remainderHigh << 2 | remainderLow >> 30;
            remainderLow = remainderLow << 2 | pair;
            uint trialLow = rootLow << 2 | 1u;
            uint trialHigh = rootHigh << 2 | rootLow >> 30;
            uint bit = 0;
            if (PairLess(remainderLow, remainderHigh, trialLow, trialHigh) == 0)
            {
                remainderHigh = unchecked(remainderHigh - trialHigh - (remainderLow < trialLow ? 1u : 0u));
                remainderLow = unchecked(remainderLow - trialLow);
                bit = 1;
            }

            rootHigh = rootHigh << 1 | rootLow >> 31;
            rootLow = rootLow << 1 | bit;
        }

        rootLow |= (remainderLow | remainderHigh) != 0 ? 1u : 0u;
        exponent = (unbiased >> 1) + 1023 + ExponentOffset - 2048;
        if (precision == 32)
        {
            uint significand = rootLow >> 29 | rootHigh << 3 | ((rootLow << 3) != 0 ? 1u : 0u);
            return RoundSingle(significand, exponent - 896, 0);
        }

        return RoundDouble(rootLow, rootHigh, exponent, 0, word);
    }

    private static uint Remainder(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh,
        uint nearest, uint precision, uint word)
    {
        if (IsNaN(leftLow, leftHigh) != 0 || IsNaN(rightLow, rightHigh) != 0 || IsInfinite(leftHigh) != 0 ||
            ((rightHigh & 0x7FFFFFFFu) | rightLow) == 0)
        {
            return Special(2, 0, precision, word);
        }

        if (((leftHigh & 0x7FFFFFFFu) | leftLow) == 0 || IsInfinite(rightHigh) != 0)
        {
            return Transport(leftLow, leftHigh, precision, word);
        }

        return RemainderFinite(Normalize(leftLow, leftHigh, 0), Normalize(leftLow, leftHigh, 1), Normalize(leftLow, leftHigh, 2),
            Normalize(rightLow, rightHigh, 0), Normalize(rightLow, rightHigh, 1), Normalize(rightLow, rightHigh, 2),
            leftHigh >> 31, nearest, precision, word);
    }

    private static uint RemainderFinite(uint low, uint high, uint exponent, uint denominatorLow, uint denominatorHigh,
        uint denominatorExponent, uint sign, uint nearest, uint precision, uint word)
    {
        if (exponent < denominatorExponent)
        {
            if (nearest == 0 || denominatorExponent - exponent > 1)
            {
                return RoundNormalized(low, high, exponent, sign, precision, word);
            }

            denominatorHigh = denominatorHigh << 1 | denominatorLow >> 31;
            denominatorLow <<= 1;
            denominatorExponent = exponent;
        }

        while (exponent > denominatorExponent)
        {
            if (PairLess(low, high, denominatorLow, denominatorHigh) == 0)
            {
                high = unchecked(high - denominatorHigh - (low < denominatorLow ? 1u : 0u));
                low = unchecked(low - denominatorLow);
            }

            high = high << 1 | low >> 31;
            low <<= 1;
            exponent--;
        }

        uint odd = 0;
        if (PairLess(low, high, denominatorLow, denominatorHigh) == 0)
        {
            high = unchecked(high - denominatorHigh - (low < denominatorLow ? 1u : 0u));
            low = unchecked(low - denominatorLow);
            odd = 1;
        }

        return FinishRemainder(low, high, denominatorLow, denominatorHigh, exponent, sign, odd, nearest, precision, word);
    }

    private static uint FinishRemainder(uint low, uint high, uint denominatorLow, uint denominatorHigh,
        uint exponent, uint sign, uint odd, uint nearest, uint precision, uint word)
    {
        uint twiceLow = low << 1;
        uint twiceHigh = high << 1 | low >> 31;
        if (nearest != 0 && (PairLess(denominatorLow, denominatorHigh, twiceLow, twiceHigh) != 0 ||
            (twiceLow == denominatorLow && twiceHigh == denominatorHigh && odd != 0)))
        {
            high = unchecked(denominatorHigh - high - (denominatorLow < low ? 1u : 0u));
            low = unchecked(denominatorLow - low);
            sign ^= 1u;
        }

        if ((low | high) == 0)
        {
            return Special(0, sign, precision, word);
        }

        while ((high & 0x100000u) == 0)
        {
            high = high << 1 | low >> 31;
            low <<= 1;
            exponent--;
        }

        return RoundNormalized(low, high, exponent, sign, precision, word);
    }

    private static uint Normalize(uint low, uint high, uint word)
    {
        uint exponent = high >> 20 & 0x7FFu;
        high = (high & 0xFFFFFu) | (exponent == 0 ? 0 : 0x100000u);
        if ((low | high) == 0)
        {
            return 0;
        }

        exponent = (exponent == 0 ? 1 : exponent) + 4096;
        while ((high & 0x100000u) == 0)
        {
            high = high << 1 | low >> 31;
            low <<= 1;
            exponent--;
        }

        return word == 0 ? low : word == 1 ? high : exponent;
    }

    private static uint PairBits(uint low, uint high, uint distance)
    {
        if (distance < 31)
        {
            return low >> (int)distance & 3u;
        }

        return distance == 31 ? low >> 31 | (high & 1u) << 1 : high >> (int)(distance - 32) & 3u;
    }

    private static uint PairLess(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        leftHigh < rightHigh || (leftHigh == rightHigh && leftLow < rightLow) ? 1u : 0u;

    private static uint IsNaN(uint low, uint high) =>
        IsInfinite(high) != 0 && ((high & 0xFFFFFu) | low) != 0 ? 1u : 0u;

    private static uint IsInfinite(uint high) => (high & 0x7FF00000u) == 0x7FF00000u ? 1u : 0u;

    private static uint Transport(uint low, uint high, uint precision, uint word) =>
        precision == 32 ? WarpPortableNumericConversions.DoubleToSingle(low, high) : word == 0 ? low : high;

    private static uint Special(uint kind, uint sign, uint precision, uint word)
    {
        if (precision == 32)
        {
            return kind == 2 ? 0x7FC00000u : sign << 31 | (kind == 1 ? 0x7F800000u : 0);
        }

        return word == 0 ? 0 : kind == 2 ? 0x7FF80000u : sign << 31 | (kind == 1 ? 0x7FF00000u : 0);
    }
}
