namespace WarpCLR.Compiler;

internal static partial class WarpPortableBinary64Math
{
    public static uint FusedMultiplyAddLow(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh, uint addendLow, uint addendHigh) =>
        FusedMultiplyAdd(leftLow, leftHigh, rightLow, rightHigh, addendLow, addendHigh, 64, 0);

    public static uint FusedMultiplyAddHigh(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh, uint addendLow, uint addendHigh) =>
        FusedMultiplyAdd(leftLow, leftHigh, rightLow, rightHigh, addendLow, addendHigh, 64, 1);

    internal static uint FusedMultiplyAddSingle(uint left, uint right, uint addend) =>
        FusedMultiplyAdd(WarpPortableNumericConversions.SingleToDoubleLow(left), WarpPortableNumericConversions.SingleToDoubleHigh(left),
            WarpPortableNumericConversions.SingleToDoubleLow(right), WarpPortableNumericConversions.SingleToDoubleHigh(right),
            WarpPortableNumericConversions.SingleToDoubleLow(addend), WarpPortableNumericConversions.SingleToDoubleHigh(addend), 32, 0);

    private static uint FusedMultiplyAdd(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh,
        uint addendLow, uint addendHigh, uint precision, uint word)
    {
        uint sign = (leftHigh ^ rightHigh) >> 31;
        bool productZero = ((leftHigh & 0x7FFFFFFFu) | leftLow) == 0 || ((rightHigh & 0x7FFFFFFFu) | rightLow) == 0;
        bool productInfinite = IsInfinite(leftHigh) != 0 || IsInfinite(rightHigh) != 0;
        if (IsNaN(leftLow, leftHigh) != 0 || IsNaN(rightLow, rightHigh) != 0 || IsNaN(addendLow, addendHigh) != 0 ||
            (productInfinite && (productZero || (IsInfinite(addendHigh) != 0 && sign != addendHigh >> 31))))
        {
            return Special(2, 0, precision, word);
        }

        if (productInfinite)
        {
            return Special(1, sign, precision, word);
        }

        if (IsInfinite(addendHigh) != 0)
        {
            return Transport(addendLow, addendHigh, precision, word);
        }

        if (productZero)
        {
            return ((addendHigh & 0x7FFFFFFFu) | addendLow) == 0
                ? Special(0, sign & (addendHigh >> 31), precision, word) : Transport(addendLow, addendHigh, precision, word);
        }

        return FmaMultiply(Normalize(leftLow, leftHigh, 0), Normalize(leftLow, leftHigh, 1), Normalize(leftLow, leftHigh, 2),
            Normalize(rightLow, rightHigh, 0), Normalize(rightLow, rightHigh, 1), Normalize(rightLow, rightHigh, 2),
            Normalize(addendLow, addendHigh, 0), Normalize(addendLow, addendHigh, 1), Normalize(addendLow, addendHigh, 2),
            sign, addendHigh >> 31, precision, word);
    }

    private static uint FmaMultiply(uint leftLow, uint leftHigh, uint leftExponent,
        uint rightLow, uint rightHigh, uint rightExponent, uint addendLow, uint addendHigh, uint addendExponent,
        uint sign, uint addendSign, uint precision, uint word)
    {
        uint bottom = unchecked(leftLow * rightLow);
        uint middleLow = MultiplyHigh(leftLow, rightLow);
        uint term = unchecked(leftLow * rightHigh);
        uint sum = unchecked(middleLow + term);
        uint carry = sum < middleLow ? 1u : 0u;
        middleLow = sum;
        term = unchecked(leftHigh * rightLow);
        sum = unchecked(middleLow + term);
        carry += sum < middleLow ? 1u : 0u;
        middleLow = sum;
        uint middleHigh = unchecked(leftHigh * rightHigh);
        uint top = MultiplyHigh(leftHigh, rightHigh);
        term = MultiplyHigh(leftLow, rightHigh);
        sum = unchecked(middleHigh + term);
        top += sum < middleHigh ? 1u : 0u;
        middleHigh = sum;
        term = MultiplyHigh(leftHigh, rightLow);
        sum = unchecked(middleHigh + term);
        top += sum < middleHigh ? 1u : 0u;
        middleHigh = sum;
        sum = unchecked(middleHigh + carry);
        top += sum < middleHigh ? 1u : 0u;
        middleHigh = sum;
        uint leading = top >> 9;
        uint shift = 22 - leading;
        top = top << (int)shift | middleHigh >> (int)(32 - shift);
        middleHigh = middleHigh << (int)shift | middleLow >> (int)(32 - shift);
        middleLow = middleLow << (int)shift | bottom >> (int)(32 - shift);
        bottom <<= (int)shift;
        return FmaAlign(bottom, middleLow, middleHigh, top, leftExponent + rightExponent - 2046 + leading,
            sign, addendLow, addendHigh, addendExponent == 0 ? 0 : addendExponent + 3073, addendSign, precision, word);
    }

    private static uint FmaAlign(uint low, uint middleLow, uint middleHigh, uint high, uint exponent, uint sign,
        uint addendLow, uint addendHigh, uint addendExponent, uint addendSign, uint precision, uint word)
    {
        uint otherLow = 0;
        uint otherMiddleLow = 0;
        uint otherMiddleHigh = addendLow << 10;
        uint otherHigh = addendHigh << 10 | addendLow >> 22;
        if (exponent < addendExponent || (exponent == addendExponent &&
            QuadLess(low, middleLow, middleHigh, high, otherLow, otherMiddleLow, otherMiddleHigh, otherHigh) != 0))
        {
            uint temporary = low;
            low = otherLow;
            otherLow = temporary;
            temporary = middleLow;
            middleLow = otherMiddleLow;
            otherMiddleLow = temporary;
            temporary = middleHigh;
            middleHigh = otherMiddleHigh;
            otherMiddleHigh = temporary;
            temporary = high;
            high = otherHigh;
            otherHigh = temporary;
            temporary = exponent;
            exponent = addendExponent;
            addendExponent = temporary;
            temporary = sign;
            sign = addendSign;
            addendSign = temporary;
        }

        uint distance = exponent - addendExponent;
        return FmaCombine(low, middleLow, middleHigh, high,
            ShiftQuadJam(otherLow, otherMiddleLow, otherMiddleHigh, otherHigh, distance, 0),
            ShiftQuadJam(otherLow, otherMiddleLow, otherMiddleHigh, otherHigh, distance, 1),
            ShiftQuadJam(otherLow, otherMiddleLow, otherMiddleHigh, otherHigh, distance, 2),
            ShiftQuadJam(otherLow, otherMiddleLow, otherMiddleHigh, otherHigh, distance, 3),
            exponent, sign, addendSign, precision, word);
    }

    private static uint FmaCombine(uint low, uint middleLow, uint middleHigh, uint high,
        uint otherLow, uint otherMiddleLow, uint otherMiddleHigh, uint otherHigh,
        uint exponent, uint sign, uint otherSign, uint precision, uint word)
    {
        uint subtract = sign ^ otherSign;
        return FmaNormalize(QuadArithmetic(low, middleLow, middleHigh, high, otherLow, otherMiddleLow, otherMiddleHigh, otherHigh, subtract, 0),
            QuadArithmetic(low, middleLow, middleHigh, high, otherLow, otherMiddleLow, otherMiddleHigh, otherHigh, subtract, 1),
            QuadArithmetic(low, middleLow, middleHigh, high, otherLow, otherMiddleLow, otherMiddleHigh, otherHigh, subtract, 2),
            QuadArithmetic(low, middleLow, middleHigh, high, otherLow, otherMiddleLow, otherMiddleHigh, otherHigh, subtract, 3),
            exponent, sign, precision, word);
    }

    private static uint FmaNormalize(uint low, uint middleLow, uint middleHigh, uint high,
        uint exponent, uint sign, uint precision, uint word)
    {
        if ((low | middleLow | middleHigh | high) == 0)
        {
            return Special(0, 0, precision, word);
        }

        if ((high & 0x80000000u) != 0)
        {
            low = low >> 1 | middleLow << 31 | (low & 1u);
            middleLow = middleLow >> 1 | middleHigh << 31;
            middleHigh = middleHigh >> 1 | high << 31;
            high >>= 1;
            exponent++;
        }

        while ((high & 0x40000000u) == 0)
        {
            high = high << 1 | middleHigh >> 31;
            middleHigh = middleHigh << 1 | middleLow >> 31;
            middleLow = middleLow << 1 | low >> 31;
            low <<= 1;
            exponent--;
        }

        if (precision == 32)
        {
            uint significand = high >> 4 | (((high << 28) | middleHigh | middleLow | low) != 0 ? 1u : 0u);
            return RoundSingle(significand, exponent + 127, sign);
        }

        uint resultLow = middleHigh >> 7 | high << 25 | (((middleHigh << 25) | middleLow | low) != 0 ? 1u : 0u);
        return RoundDouble(resultLow, high >> 7, exponent + 1023, sign, word);
    }
}
