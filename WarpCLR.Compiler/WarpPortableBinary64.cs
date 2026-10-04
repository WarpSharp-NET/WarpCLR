namespace WarpCLR.Compiler;

internal static partial class WarpPortableBinary64
{
    internal const string Semantics = "warp.math.binary64.arithmetic/rne-gradual-canonical-nan/0.1";

    public static uint AddLow(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        Add(leftLow, leftHigh, rightLow, rightHigh, 0);

    public static uint AddHigh(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        Add(leftLow, leftHigh, rightLow, rightHigh, 1);

    public static uint SubtractLow(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        Add(leftLow, leftHigh, rightLow, rightHigh ^ 0x80000000u, 0);

    public static uint SubtractHigh(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        Add(leftLow, leftHigh, rightLow, rightHigh ^ 0x80000000u, 1);

    private static uint Add(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh, uint word)
    {
        uint leftExponent = (leftHigh >> 20) & 0x7FFu;
        uint rightExponent = (rightHigh >> 20) & 0x7FFu;
        if (leftExponent == 0x7FFu || rightExponent == 0x7FFu)
        {
            return AddNonfinite(leftLow, leftHigh, rightLow, rightHigh, word);
        }

        uint leftSign = leftHigh >> 31;
        uint rightSign = rightHigh >> 31;
        leftHigh = ((leftHigh & 0xFFFFFu) | (leftExponent == 0 ? 0 : 0x100000u)) << 3 | leftLow >> 29;
        rightHigh = ((rightHigh & 0xFFFFFu) | (rightExponent == 0 ? 0 : 0x100000u)) << 3 | rightLow >> 29;
        leftLow <<= 3;
        rightLow <<= 3;
        leftExponent = leftExponent == 0 ? 1 : leftExponent;
        rightExponent = rightExponent == 0 ? 1 : rightExponent;
        if (leftExponent < rightExponent || (leftExponent == rightExponent &&
            (leftHigh < rightHigh || (leftHigh == rightHigh && leftLow < rightLow))))
        {
            uint temporary = leftLow;
            leftLow = rightLow;
            rightLow = temporary;
            temporary = leftHigh;
            leftHigh = rightHigh;
            rightHigh = temporary;
            temporary = leftExponent;
            leftExponent = rightExponent;
            rightExponent = temporary;
            temporary = leftSign;
            leftSign = rightSign;
            rightSign = temporary;
        }

        uint distance = leftExponent - rightExponent;
        return AddAligned(leftLow, leftHigh,
            ShiftRightJamLow(rightLow, rightHigh, distance), ShiftRightJamHigh(rightHigh, distance),
            leftExponent, leftSign, rightSign, word);
    }

    private static uint AddAligned(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh,
        uint exponent, uint leftSign, uint rightSign, uint word)
    {
        if (leftSign == rightSign)
        {
            uint sum = unchecked(leftLow + rightLow);
            leftHigh = unchecked(leftHigh + rightHigh + (sum < leftLow ? 1u : 0u));
            leftLow = sum;
            if ((leftHigh & 0x1000000u) != 0)
            {
                leftLow = ShiftRightJamLow(leftLow, leftHigh, 1);
                leftHigh >>= 1;
                exponent++;
            }
        }
        else
        {
            leftHigh = unchecked(leftHigh - rightHigh - (leftLow < rightLow ? 1u : 0u));
            leftLow = unchecked(leftLow - rightLow);
            if ((leftLow | leftHigh) == 0)
            {
                return 0;
            }
            while ((leftHigh & 0x800000u) == 0 && exponent > 1)
            {
                leftHigh = leftHigh << 1 | leftLow >> 31;
                leftLow <<= 1;
                exponent--;
            }
        }

        return Round(leftLow, leftHigh, exponent, leftSign, word);
    }

    private static uint Round(uint low, uint high, uint exponent, uint sign, uint word)
    {
        uint roundBits = low & 7u;
        low = low >> 3 | high << 29;
        high >>= 3;
        if (roundBits > 4 || (roundBits == 4 && (low & 1u) != 0))
        {
            low = unchecked(low + 1);
            high += low == 0 ? 1u : 0u;
            if ((high & 0x200000u) != 0)
            {
                low = 0;
                high = 0x100000u;
                exponent++;
            }
        }

        if ((high & 0x100000u) == 0)
        {
            exponent = 0;
        }

        if (exponent >= 0x7FFu)
        {
            return word == 0 ? 0 : sign << 31 | 0x7FF00000u;
        }
        return word == 0 ? low : sign << 31 | exponent << 20 | high & 0xFFFFFu;
    }

    private static uint AddNonfinite(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh, uint word)
    {
        bool leftInfinite = (leftHigh & 0x7FF00000u) == 0x7FF00000u;
        bool rightInfinite = (rightHigh & 0x7FF00000u) == 0x7FF00000u;
        bool leftNan = leftInfinite && ((leftHigh & 0xFFFFFu) | leftLow) != 0;
        bool rightNan = rightInfinite && ((rightHigh & 0xFFFFFu) | rightLow) != 0;
        if (leftNan || rightNan || (leftInfinite && rightInfinite && ((leftHigh ^ rightHigh) >> 31) != 0))
        {
            return word == 0 ? 0 : 0x7FF80000u;
        }

        return word == 0 ? 0 : (leftInfinite ? leftHigh : rightHigh);
    }

    private static uint ShiftRightJamLow(uint low, uint high, uint distance)
    {
        if (distance == 0)
        {
            return low;
        }
        if (distance < 32)
        {
            uint jam = (low << (int)(32 - distance)) != 0 ? 1u : 0u;
            return low >> (int)distance | high << (int)(32 - distance) | jam;
        }

        if (distance == 32)
        {
            return high | (low != 0 ? 1u : 0u);
        }
        if (distance < 64)
        {
            distance -= 32;
            uint jam = ((high << (int)(32 - distance)) | low) != 0 ? 1u : 0u;
            return high >> (int)distance | jam;
        }

        return (low | high) != 0 ? 1u : 0u;
    }

    private static uint ShiftRightJamHigh(uint high, uint distance) =>
        distance < 32 ? high >> (int)distance : 0;
}
