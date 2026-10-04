namespace WarpCLR.Compiler;

internal static class WarpPortableBinary32
{
    internal const string Semantics = "warp.math.binary32.arithmetic/rne-gradual-canonical-nan/0.1";
    private const uint ExponentOffset = 512;

    public static uint Add(uint left, uint right)
    {
        uint leftExponent = left >> 23 & 0xFFu;
        uint rightExponent = right >> 23 & 0xFFu;
        if (leftExponent == 0xFFu || rightExponent == 0xFFu)
        {
            return AddNonfinite(left, right);
        }

        uint leftSign = left >> 31;
        uint rightSign = right >> 31;
        left = ((left & 0x7FFFFFu) | (leftExponent == 0 ? 0 : 0x800000u)) << 3;
        right = ((right & 0x7FFFFFu) | (rightExponent == 0 ? 0 : 0x800000u)) << 3;
        leftExponent = leftExponent == 0 ? 1 : leftExponent;
        rightExponent = rightExponent == 0 ? 1 : rightExponent;
        if (leftExponent < rightExponent || (leftExponent == rightExponent && left < right))
        {
            uint temporary = left;
            left = right;
            right = temporary;
            temporary = leftExponent;
            leftExponent = rightExponent;
            rightExponent = temporary;
            temporary = leftSign;
            leftSign = rightSign;
            rightSign = temporary;
        }

        return AddAligned(left, ShiftRightJam(right, leftExponent - rightExponent), leftExponent, leftSign, rightSign);
    }

    public static uint Subtract(uint left, uint right) => Add(left, right ^ 0x80000000u);

    private static uint AddAligned(uint left, uint right, uint exponent, uint leftSign, uint rightSign)
    {
        if (leftSign == rightSign)
        {
            left += right;
            if ((left & 0x8000000u) != 0)
            {
                left = ShiftRightJam(left, 1);
                exponent++;
            }
        }
        else
        {
            left -= right;
            if (left == 0)
            {
                return 0;
            }

            while ((left & 0x4000000u) == 0 && exponent > 1)
            {
                left <<= 1;
                exponent--;
            }
        }

        return Round(left, exponent, leftSign);
    }

    public static uint Multiply(uint left, uint right)
    {
        uint leftExponent = left >> 23 & 0xFFu;
        uint rightExponent = right >> 23 & 0xFFu;
        uint sign = (left ^ right) >> 31;
        bool leftZero = (left & 0x7FFFFFFFu) == 0;
        bool rightZero = (right & 0x7FFFFFFFu) == 0;
        if (leftExponent == 0xFFu || rightExponent == 0xFFu)
        {
            bool nan = (leftExponent == 0xFFu && (left & 0x7FFFFFu) != 0) ||
                (rightExponent == 0xFFu && (right & 0x7FFFFFu) != 0) || leftZero || rightZero;
            return nan ? 0x7FC00000u : sign << 31 | 0x7F800000u;
        }

        if (leftZero || rightZero)
        {
            return sign << 31;
        }

        return MultiplyFinite(left & 0x7FFFFFu, right & 0x7FFFFFu, leftExponent, rightExponent, sign);
    }

    private static uint MultiplyFinite(uint left, uint right, uint leftExponent, uint rightExponent, uint sign)
    {
        left |= leftExponent == 0 ? 0 : 0x800000u;
        right |= rightExponent == 0 ? 0 : 0x800000u;
        leftExponent = (leftExponent == 0 ? 1 : leftExponent) + ExponentOffset;
        rightExponent = (rightExponent == 0 ? 1 : rightExponent) + ExponentOffset;
        while ((left & 0x800000u) == 0)
        {
            left <<= 1;
            leftExponent--;
        }

        while ((right & 0x800000u) == 0)
        {
            right <<= 1;
            rightExponent--;
        }

        uint low = unchecked(left * right);
        uint high = WarpPortableWordMath.MultiplyHigh(left, right);
        uint leading = high >> 15;
        uint shift = 20 + leading;
        uint exponent = leftExponent + rightExponent - (ExponentOffset + 127) + leading;
        uint significand = low >> (int)shift | high << (int)(32 - shift);
        significand |= (low << (int)(32 - shift)) != 0 ? 1u : 0u;
        return RoundWide(significand, exponent, sign);
    }

    public static uint Divide(uint left, uint right)
    {
        uint leftExponent = left >> 23 & 0xFFu;
        uint rightExponent = right >> 23 & 0xFFu;
        uint sign = (left ^ right) >> 31;
        bool leftZero = (left & 0x7FFFFFFFu) == 0;
        bool rightZero = (right & 0x7FFFFFFFu) == 0;
        bool nan = (leftExponent == 0xFFu && (left & 0x7FFFFFu) != 0) ||
            (rightExponent == 0xFFu && (right & 0x7FFFFFu) != 0) ||
            (leftExponent == 0xFFu && rightExponent == 0xFFu) || (leftZero && rightZero);
        if (nan)
        {
            return 0x7FC00000u;
        }

        if (leftExponent == 0xFFu || rightZero)
        {
            return sign << 31 | 0x7F800000u;
        }

        if (leftZero || rightExponent == 0xFFu)
        {
            return sign << 31;
        }

        return DivideFinite(left & 0x7FFFFFu, right & 0x7FFFFFu, leftExponent, rightExponent, sign);
    }

    private static uint DivideFinite(uint left, uint right, uint leftExponent, uint rightExponent, uint sign)
    {
        left |= leftExponent == 0 ? 0 : 0x800000u;
        right |= rightExponent == 0 ? 0 : 0x800000u;
        leftExponent = (leftExponent == 0 ? 1 : leftExponent) + ExponentOffset;
        rightExponent = (rightExponent == 0 ? 1 : rightExponent) + ExponentOffset;
        while ((left & 0x800000u) == 0)
        {
            left <<= 1;
            leftExponent--;
        }

        while ((right & 0x800000u) == 0)
        {
            right <<= 1;
            rightExponent--;
        }

        uint exponent = leftExponent + (ExponentOffset + 127) - rightExponent;
        if (left < right)
        {
            left <<= 1;
            exponent--;
        }

        uint quotient = 0;
        for (uint bit = 0; bit < 27; bit++)
        {
            uint next = 0;
            if (left >= right)
            {
                left -= right;
                next = 1;
            }

            quotient = quotient << 1 | next;
            left <<= 1;
        }

        return RoundWide(quotient | (left != 0 ? 1u : 0u), exponent, sign);
    }

    private static uint RoundWide(uint significand, uint exponent, uint sign)
    {
        if (exponent <= ExponentOffset)
        {
            significand = ShiftRightJam(significand, ExponentOffset + 1 - exponent);
            exponent = 1;
        }
        else
        {
            exponent -= ExponentOffset;
        }

        return Round(significand, exponent, sign);
    }

    private static uint Round(uint significand, uint exponent, uint sign)
    {
        uint roundBits = significand & 7u;
        significand >>= 3;
        if (roundBits > 4 || (roundBits == 4 && (significand & 1u) != 0))
        {
            significand++;
            if ((significand & 0x1000000u) != 0)
            {
                significand >>= 1;
                exponent++;
            }
        }

        if ((significand & 0x800000u) == 0)
        {
            exponent = 0;
        }

        return exponent >= 0xFFu ? sign << 31 | 0x7F800000u :
            sign << 31 | exponent << 23 | significand & 0x7FFFFFu;
    }

    private static uint AddNonfinite(uint left, uint right)
    {
        bool leftInfinite = (left & 0x7F800000u) == 0x7F800000u;
        bool rightInfinite = (right & 0x7F800000u) == 0x7F800000u;
        if ((leftInfinite && (left & 0x7FFFFFu) != 0) || (rightInfinite && (right & 0x7FFFFFu) != 0) ||
            (leftInfinite && rightInfinite && ((left ^ right) >> 31) != 0))
        {
            return 0x7FC00000u;
        }

        return leftInfinite ? left : right;
    }

    private static uint ShiftRightJam(uint value, uint distance)
    {
        if (distance == 0)
        {
            return value;
        }

        return distance < 32 ? value >> (int)distance | ((value << (int)(32 - distance)) != 0 ? 1u : 0u) :
            value != 0 ? 1u : 0u;
    }
}
