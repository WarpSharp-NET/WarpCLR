namespace WarpCLR.Compiler;

internal static partial class WarpPortableBinary64
{
    private const uint ExponentOffset = 4096;

    public static uint MultiplyLow(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        Multiply(leftLow, leftHigh, rightLow, rightHigh, 0);

    public static uint MultiplyHigh(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        Multiply(leftLow, leftHigh, rightLow, rightHigh, 1);

    public static uint DivideLow(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        Divide(leftLow, leftHigh, rightLow, rightHigh, 0);

    public static uint DivideHigh(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        Divide(leftLow, leftHigh, rightLow, rightHigh, 1);

    private static uint Multiply(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh, uint word)
    {
        uint leftExponent = (leftHigh >> 20) & 0x7FFu;
        uint rightExponent = (rightHigh >> 20) & 0x7FFu;
        uint sign = (leftHigh ^ rightHigh) >> 31;
        bool leftZero = ((leftHigh & 0x7FFFFFFFu) | leftLow) == 0;
        bool rightZero = ((rightHigh & 0x7FFFFFFFu) | rightLow) == 0;
        if (leftExponent == 0x7FFu || rightExponent == 0x7FFu)
        {
            bool nan = (leftExponent == 0x7FFu && ((leftHigh & 0xFFFFFu) | leftLow) != 0) ||
                (rightExponent == 0x7FFu && ((rightHigh & 0xFFFFFu) | rightLow) != 0) || leftZero || rightZero;
            return word == 0 ? 0 : nan ? 0x7FF80000u : sign << 31 | 0x7FF00000u;
        }

        if (leftZero || rightZero)
        {
            return word == 0 ? 0 : sign << 31;
        }

        return MultiplyFinite(leftLow, leftHigh & 0xFFFFFu, rightLow, rightHigh & 0xFFFFFu,
            leftExponent, rightExponent, sign, word);
    }

    private static uint MultiplyFinite(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh,
        uint leftExponent, uint rightExponent, uint sign, uint word)
    {
        leftHigh |= leftExponent == 0 ? 0 : 0x100000u;
        rightHigh |= rightExponent == 0 ? 0 : 0x100000u;
        leftExponent = (leftExponent == 0 ? 1 : leftExponent) + ExponentOffset;
        rightExponent = (rightExponent == 0 ? 1 : rightExponent) + ExponentOffset;
        while ((leftHigh & 0x100000u) == 0)
        {
            leftHigh = leftHigh << 1 | leftLow >> 31;
            leftLow <<= 1;
            leftExponent--;
        }

        while ((rightHigh & 0x100000u) == 0)
        {
            rightHigh = rightHigh << 1 | rightLow >> 31;
            rightLow <<= 1;
            rightExponent--;
        }

        return MultiplySignificands(leftLow, leftHigh, rightLow, rightHigh,
            leftExponent + rightExponent - (ExponentOffset + 1023), sign, word);
    }

    private static uint MultiplySignificands(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh,
        uint exponent, uint sign, uint word)
    {
        uint bottom = unchecked(leftLow * rightLow);
        uint middleLow = WarpPortableWordMath.MultiplyHigh(leftLow, rightLow);
        uint term = unchecked(leftLow * rightHigh);
        uint sum = unchecked(middleLow + term);
        uint carry = sum < middleLow ? 1u : 0u;
        middleLow = sum;
        term = unchecked(leftHigh * rightLow);
        sum = unchecked(middleLow + term);
        carry += sum < middleLow ? 1u : 0u;
        middleLow = sum;
        uint middleHigh = unchecked(leftHigh * rightHigh);
        uint top = WarpPortableWordMath.MultiplyHigh(leftHigh, rightHigh);
        term = WarpPortableWordMath.MultiplyHigh(leftLow, rightHigh);
        sum = unchecked(middleHigh + term);
        top += sum < middleHigh ? 1u : 0u;
        middleHigh = sum;
        term = WarpPortableWordMath.MultiplyHigh(leftHigh, rightLow);
        sum = unchecked(middleHigh + term);
        top += sum < middleHigh ? 1u : 0u;
        middleHigh = sum;
        sum = unchecked(middleHigh + carry);
        top += sum < middleHigh ? 1u : 0u;
        middleHigh = sum;
        uint leading = top >> 9;
        uint shift = 17 + leading;
        exponent += leading;
        uint low = middleLow >> (int)shift | middleHigh << (int)(32 - shift);
        uint high = middleHigh >> (int)shift | top << (int)(32 - shift);
        low |= ((middleLow << (int)(32 - shift)) | bottom) != 0 ? 1u : 0u;
        return RoundWide(low, high, exponent, sign, word);
    }

    private static uint Divide(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh, uint word)
    {
        uint leftExponent = (leftHigh >> 20) & 0x7FFu;
        uint rightExponent = (rightHigh >> 20) & 0x7FFu;
        uint sign = (leftHigh ^ rightHigh) >> 31;
        bool leftZero = ((leftHigh & 0x7FFFFFFFu) | leftLow) == 0;
        bool rightZero = ((rightHigh & 0x7FFFFFFFu) | rightLow) == 0;
        bool nan = (leftExponent == 0x7FFu && ((leftHigh & 0xFFFFFu) | leftLow) != 0) ||
            (rightExponent == 0x7FFu && ((rightHigh & 0xFFFFFu) | rightLow) != 0) ||
            (leftExponent == 0x7FFu && rightExponent == 0x7FFu) || (leftZero && rightZero);
        if (nan)
        {
            return word == 0 ? 0 : 0x7FF80000u;
        }

        if (leftExponent == 0x7FFu || rightZero)
        {
            return word == 0 ? 0 : sign << 31 | 0x7FF00000u;
        }

        if (leftZero || rightExponent == 0x7FFu)
        {
            return word == 0 ? 0 : sign << 31;
        }

        return DivideFinite(leftLow, leftHigh & 0xFFFFFu, rightLow, rightHigh & 0xFFFFFu,
            leftExponent, rightExponent, sign, word);
    }

    private static uint DivideFinite(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh,
        uint leftExponent, uint rightExponent, uint sign, uint word)
    {
        leftHigh |= leftExponent == 0 ? 0 : 0x100000u;
        rightHigh |= rightExponent == 0 ? 0 : 0x100000u;
        leftExponent = (leftExponent == 0 ? 1 : leftExponent) + ExponentOffset;
        rightExponent = (rightExponent == 0 ? 1 : rightExponent) + ExponentOffset;
        while ((leftHigh & 0x100000u) == 0)
        {
            leftHigh = leftHigh << 1 | leftLow >> 31;
            leftLow <<= 1;
            leftExponent--;
        }

        while ((rightHigh & 0x100000u) == 0)
        {
            rightHigh = rightHigh << 1 | rightLow >> 31;
            rightLow <<= 1;
            rightExponent--;
        }

        uint exponent = leftExponent + (ExponentOffset + 1023) - rightExponent;
        if (leftHigh < rightHigh || (leftHigh == rightHigh && leftLow < rightLow))
        {
            leftHigh = leftHigh << 1 | leftLow >> 31;
            leftLow <<= 1;
            exponent--;
        }

        return DivideSignificands(leftLow, leftHigh, rightLow, rightHigh, exponent, sign, word);
    }

    private static uint DivideSignificands(uint low, uint high, uint denominatorLow, uint denominatorHigh,
        uint exponent, uint sign, uint word)
    {
        uint quotientLow = 0;
        uint quotientHigh = 0;
        for (uint bit = 0; bit < 56; bit++)
        {
            uint next = 0;
            if (high > denominatorHigh || (high == denominatorHigh && low >= denominatorLow))
            {
                high = unchecked(high - denominatorHigh - (low < denominatorLow ? 1u : 0u));
                low = unchecked(low - denominatorLow);
                next = 1;
            }

            quotientHigh = quotientHigh << 1 | quotientLow >> 31;
            quotientLow = quotientLow << 1 | next;
            high = high << 1 | low >> 31;
            low <<= 1;
        }

        quotientLow |= (low | high) != 0 ? 1u : 0u;
        return RoundWide(quotientLow, quotientHigh, exponent, sign, word);
    }

    private static uint RoundWide(uint low, uint high, uint exponent, uint sign, uint word)
    {
        if (exponent <= ExponentOffset)
        {
            uint distance = ExponentOffset + 1 - exponent;
            low = ShiftRightJamLow(low, high, distance);
            high = ShiftRightJamHigh(high, distance);
            exponent = 1;
        }
        else
        {
            exponent -= ExponentOffset;
        }

        return Round(low, high, exponent, sign, word);
    }
}
