namespace WarpCLR.Compiler;

internal static partial class WarpPortableBinary64Math
{
    private static uint MultiplyHigh(uint left, uint right)
    {
        uint leftLow = left & 0xFFFFu;
        uint rightLow = right & 0xFFFFu;
        uint leftHigh = left >> 16;
        uint rightHigh = right >> 16;
        uint bottom = leftLow * rightLow;
        uint cross = leftHigh * rightLow + (bottom >> 16);
        uint middle = (cross & 0xFFFFu) + leftLow * rightHigh;
        return leftHigh * rightHigh + (cross >> 16) + (middle >> 16);
    }

    private static uint QuadWord(uint low, uint middleLow, uint middleHigh, uint high, uint word) =>
        word == 0 ? low : word == 1 ? middleLow : word == 2 ? middleHigh : word == 3 ? high : 0;

    private static uint QuadLess(uint leftLow, uint leftMiddleLow, uint leftMiddleHigh, uint leftHigh,
        uint rightLow, uint rightMiddleLow, uint rightMiddleHigh, uint rightHigh) =>
        leftHigh < rightHigh || (leftHigh == rightHigh && (leftMiddleHigh < rightMiddleHigh ||
            (leftMiddleHigh == rightMiddleHigh && (leftMiddleLow < rightMiddleLow ||
                (leftMiddleLow == rightMiddleLow && leftLow < rightLow))))) ? 1u : 0u;

    private static uint QuadArithmetic(uint leftLow, uint leftMiddleLow, uint leftMiddleHigh, uint leftHigh,
        uint rightLow, uint rightMiddleLow, uint rightMiddleHigh, uint rightHigh, uint subtract, uint word)
    {
        uint carry = 0;
        uint result = 0;
        for (uint index = 0; index <= word; index++)
        {
            uint left = QuadWord(leftLow, leftMiddleLow, leftMiddleHigh, leftHigh, index);
            uint right = QuadWord(rightLow, rightMiddleLow, rightMiddleHigh, rightHigh, index);
            if (subtract == 0)
            {
                uint sum = unchecked(left + right);
                result = unchecked(sum + carry);
                carry = (sum < left || result < sum) ? 1u : 0u;
            }
            else
            {
                uint difference = unchecked(left - right);
                result = unchecked(difference - carry);
                carry = (left < right || difference < carry) ? 1u : 0u;
            }
        }

        return result;
    }

    private static uint ShiftQuadJam(uint low, uint middleLow, uint middleHigh, uint high, uint distance, uint word)
    {
        if (distance >= 128)
        {
            return word == 0 && (low | middleLow | middleHigh | high) != 0 ? 1u : 0u;
        }

        uint whole = distance >> 5;
        uint bits = distance & 31u;
        uint result = QuadWord(low, middleLow, middleHigh, high, word + whole) >> (int)bits;
        if (bits != 0)
        {
            result |= QuadWord(low, middleLow, middleHigh, high, word + whole + 1) << (int)(32 - bits);
        }

        if (word == 0)
        {
            uint lost = 0;
            for (uint index = 0; index < whole; index++)
            {
                lost |= QuadWord(low, middleLow, middleHigh, high, index);
            }

            if (bits != 0)
            {
                lost |= QuadWord(low, middleLow, middleHigh, high, whole) << (int)(32 - bits);
            }

            result |= lost != 0 ? 1u : 0u;
        }

        return result;
    }

    private static uint RoundNormalized(uint low, uint high, uint exponent, uint sign, uint precision, uint word)
    {
        if (precision == 32)
        {
            uint significand = low >> 26 | high << 6 | ((low << 6) != 0 ? 1u : 0u);
            return RoundSingle(significand, exponent + 4096 - 896, sign);
        }

        return RoundDouble(low << 3, high << 3 | low >> 29, exponent + 4096, sign, word);
    }

    private static uint RoundSingle(uint significand, uint exponent, uint sign)
    {
        if (exponent <= ExponentOffset)
        {
            significand = ShiftSingleJam(significand, ExponentOffset + 1 - exponent);
            exponent = 1;
        }
        else
        {
            exponent -= ExponentOffset;
        }

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

        return exponent >= 0xFFu ? sign << 31 | 0x7F800000u : sign << 31 | exponent << 23 | significand & 0x7FFFFFu;
    }

    private static uint RoundDouble(uint low, uint high, uint exponent, uint sign, uint word)
    {
        if (exponent <= ExponentOffset)
        {
            uint distance = ExponentOffset + 1 - exponent;
            low = ShiftPairJamLow(low, high, distance);
            high = distance < 32 ? high >> (int)distance : 0;
            exponent = 1;
        }
        else
        {
            exponent -= ExponentOffset;
        }

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

        return exponent >= 0x7FFu ? Special(1, sign, 64, word) :
            word == 0 ? low : sign << 31 | exponent << 20 | high & 0xFFFFFu;
    }

    private static uint ShiftSingleJam(uint value, uint distance)
    {
        if (distance == 0)
        {
            return value;
        }

        return distance < 32 ? value >> (int)distance | ((value << (int)(32 - distance)) != 0 ? 1u : 0u) : value != 0 ? 1u : 0u;
    }

    private static uint ShiftPairJamLow(uint low, uint high, uint distance)
    {
        if (distance == 0)
        {
            return low;
        }

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
