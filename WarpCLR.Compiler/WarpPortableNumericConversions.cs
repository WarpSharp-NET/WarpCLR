namespace WarpCLR.Compiler;

internal static class WarpPortableNumericConversions
{
    internal const string Semantics = "warp.math.binary32-binary64.conversions/rne-gradual-truncate-saturate-nan-zero/0.1";
    internal const uint NoFault = 0;
    internal const uint OverflowFault = 1;
    private const uint ExponentOffset = 4096;

    // Numerical width conversions canonicalize NaNs. Raw transport uses no conversion.
    public static uint SingleToDoubleLow(uint value) => SingleToDouble(value, 0);

    public static uint SingleToDoubleHigh(uint value) => SingleToDouble(value, 1);

    public static uint DoubleToSingle(uint low, uint high)
    {
        uint exponent = high >> 20 & 0x7FFu;
        uint sign = high >> 31;
        if (exponent == 0x7FFu)
        {
            return ((high & 0xFFFFFu) | low) != 0 ? 0x7FC00000u : sign << 31 | 0x7F800000u;
        }

        high = (high & 0xFFFFFu) | (exponent == 0 ? 0 : 0x100000u);
        if ((low | high) == 0)
        {
            return sign << 31;
        }

        exponent = (exponent == 0 ? 1 : exponent) + (ExponentOffset - 896);
        while ((high & 0x100000u) == 0)
        {
            high = high << 1 | low >> 31;
            low <<= 1;
            exponent--;
        }

        uint significand = low >> 26 | high << 6 | ((low << 6) != 0 ? 1u : 0u);
        if (exponent <= ExponentOffset)
        {
            significand = ShiftRightJam(significand, ExponentOffset + 1 - exponent);
            exponent = 1;
        }
        else
        {
            exponent -= ExponentOffset;
        }

        return RoundSingle(significand, exponent, sign);
    }

    public static uint UInt32ToSingle(uint value) => IntegerToSingle(value, 0, 0);

    public static uint Int32ToSingle(uint value) => IntegerToSingle(value, value >> 31 != 0 ? 0xFFFFFFFFu : 0, 1);

    public static uint UInt64ToSingle(uint low, uint high) => IntegerToSingle(low, high, 0);

    public static uint Int64ToSingle(uint low, uint high) => IntegerToSingle(low, high, 1);

    public static uint UInt32ToDoubleLow(uint value) => IntegerToDouble(value, 0, 0, 0);

    public static uint UInt32ToDoubleHigh(uint value) => IntegerToDouble(value, 0, 0, 1);

    public static uint Int32ToDoubleLow(uint value) => IntegerToDouble(value, value >> 31 != 0 ? 0xFFFFFFFFu : 0, 1, 0);

    public static uint Int32ToDoubleHigh(uint value) => IntegerToDouble(value, value >> 31 != 0 ? 0xFFFFFFFFu : 0, 1, 1);

    public static uint UInt64ToDoubleLow(uint low, uint high) => IntegerToDouble(low, high, 0, 0);

    public static uint UInt64ToDoubleHigh(uint low, uint high) => IntegerToDouble(low, high, 0, 1);

    public static uint Int64ToDoubleLow(uint low, uint high) => IntegerToDouble(low, high, 1, 0);

    public static uint Int64ToDoubleHigh(uint low, uint high) => IntegerToDouble(low, high, 1, 1);

    // The value methods implement unchecked truncation and saturation, with NaN -> zero.
    // Checked lowering must consult the corresponding fault before publishing the value.
    // Range checks apply after truncation, including negative fractions in (-1, 0).
    public static uint SingleToInt32(uint value) => SingleToInteger(value, 32, 1, 0);

    public static uint SingleToInt32Fault(uint value) => SingleToInteger(value, 32, 1, 2);

    public static uint SingleToUInt32(uint value) => SingleToInteger(value, 32, 0, 0);

    public static uint SingleToUInt32Fault(uint value) => SingleToInteger(value, 32, 0, 2);

    public static uint SingleToInt64Low(uint value) => SingleToInteger(value, 64, 1, 0);

    public static uint SingleToInt64High(uint value) => SingleToInteger(value, 64, 1, 1);

    public static uint SingleToInt64Fault(uint value) => SingleToInteger(value, 64, 1, 2);

    public static uint SingleToUInt64Low(uint value) => SingleToInteger(value, 64, 0, 0);

    public static uint SingleToUInt64High(uint value) => SingleToInteger(value, 64, 0, 1);

    public static uint SingleToUInt64Fault(uint value) => SingleToInteger(value, 64, 0, 2);

    public static uint DoubleToInt32(uint low, uint high) => DoubleToInteger(low, high, 32, 1, 0);

    public static uint DoubleToInt32Fault(uint low, uint high) => DoubleToInteger(low, high, 32, 1, 2);

    public static uint DoubleToUInt32(uint low, uint high) => DoubleToInteger(low, high, 32, 0, 0);

    public static uint DoubleToUInt32Fault(uint low, uint high) => DoubleToInteger(low, high, 32, 0, 2);

    public static uint DoubleToInt64Low(uint low, uint high) => DoubleToInteger(low, high, 64, 1, 0);

    public static uint DoubleToInt64High(uint low, uint high) => DoubleToInteger(low, high, 64, 1, 1);

    public static uint DoubleToInt64Fault(uint low, uint high) => DoubleToInteger(low, high, 64, 1, 2);

    public static uint DoubleToUInt64Low(uint low, uint high) => DoubleToInteger(low, high, 64, 0, 0);

    public static uint DoubleToUInt64High(uint low, uint high) => DoubleToInteger(low, high, 64, 0, 1);

    public static uint DoubleToUInt64Fault(uint low, uint high) => DoubleToInteger(low, high, 64, 0, 2);

    private static uint SingleToDouble(uint value, uint word)
    {
        uint exponent = value >> 23 & 0xFFu;
        uint sign = value >> 31;
        uint fraction = value & 0x7FFFFFu;
        if (exponent == 0xFFu)
        {
            return word == 0 ? 0 : fraction != 0 ? 0x7FF80000u : sign << 31 | 0x7FF00000u;
        }

        if (exponent == 0)
        {
            if (fraction == 0)
            {
                return word == 0 ? 0 : sign << 31;
            }

            exponent = 897;
            while ((fraction & 0x800000u) == 0)
            {
                fraction <<= 1;
                exponent--;
            }

            fraction &= 0x7FFFFFu;
        }
        else
        {
            exponent += 896;
        }

        return word == 0 ? fraction << 29 : sign << 31 | exponent << 20 | fraction >> 3;
    }

    private static uint IntegerToSingle(uint low, uint high, uint signed)
    {
        uint sign = signed & high >> 31;
        if (sign != 0)
        {
            low = unchecked(~low + 1);
            high = unchecked(~high + (low == 0 ? 1u : 0u));
        }

        if ((low | high) == 0)
        {
            return 0;
        }

        uint exponent = 190;
        while ((high & 0x80000000u) == 0)
        {
            high = high << 1 | low >> 31;
            low <<= 1;
            exponent--;
        }

        uint significand = high >> 5 | (((high << 27) | low) != 0 ? 1u : 0u);
        return RoundSingle(significand, exponent, sign);
    }

    private static uint IntegerToDouble(uint low, uint high, uint signed, uint word)
    {
        uint sign = signed & high >> 31;
        if (sign != 0)
        {
            low = unchecked(~low + 1);
            high = unchecked(~high + (low == 0 ? 1u : 0u));
        }

        if ((low | high) == 0)
        {
            return 0;
        }

        uint exponent = 1086;
        while ((high & 0x80000000u) == 0)
        {
            high = high << 1 | low >> 31;
            low <<= 1;
            exponent--;
        }

        low = low >> 8 | high << 24 | ((low << 24) != 0 ? 1u : 0u);
        high >>= 8;
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

        return word == 0 ? low : sign << 31 | exponent << 20 | high & 0xFFFFFu;
    }

    private static uint SingleToInteger(uint value, uint width, uint signed, uint word) =>
        DoubleToInteger(SingleToDoubleLow(value), SingleToDoubleHigh(value), width, signed, word);

    private static uint DoubleToInteger(uint low, uint high, uint width, uint signed, uint word)
    {
        uint sign = high >> 31;
        uint exponent = high >> 20 & 0x7FFu;
        high &= 0xFFFFFu;
        if (exponent == 0x7FFu)
        {
            return InvalidInteger(sign, width, signed, word, (low | high) != 0 ? 1u : 0u);
        }

        if (exponent < 1023)
        {
            return 0;
        }

        if (exponent >= 1087)
        {
            return InvalidInteger(sign, width, signed, word, 0);
        }

        high |= 0x100000u;
        uint magnitudeLow = TruncateMagnitude(low, high, exponent, 0);
        high = TruncateMagnitude(low, high, exponent, 1);
        low = magnitudeLow;

        if (IntegerOutOfRange(low, high, sign, width, signed) != 0)
        {
            return InvalidInteger(sign, width, signed, word, 0);
        }

        if (word == 2)
        {
            return NoFault;
        }

        if (sign != 0)
        {
            low = unchecked(~low + 1);
            high = unchecked(~high + (low == 0 ? 1u : 0u));
        }

        return word == 0 ? low : high;
    }

    private static uint TruncateMagnitude(uint low, uint high, uint exponent, uint word)
    {
        if (exponent < 1075)
        {
            uint distance = 1075 - exponent;
            if (distance >= 32)
            {
                return word == 0 ? high >> (int)(distance - 32) : 0;
            }

            return word == 0 ? low >> (int)distance | high << (int)(32 - distance) : high >> (int)distance;
        }

        uint shift = exponent - 1075;
        if (shift == 0)
        {
            return word == 0 ? low : high;
        }

        return word == 0 ? low << (int)shift : high << (int)shift | low >> (int)(32 - shift);
    }

    private static uint IntegerOutOfRange(uint low, uint high, uint sign, uint width, uint signed)
    {
        if (signed == 0)
        {
            return (sign != 0 && (low | high) != 0) || (width == 32 && high != 0) ? 1u : 0u;
        }

        if (width == 32)
        {
            return high != 0 || low > (sign == 0 ? 0x7FFFFFFFu : 0x80000000u) ? 1u : 0u;
        }

        return high > (sign == 0 ? 0x7FFFFFFFu : 0x80000000u) ||
            (sign != 0 && high == 0x80000000u && low != 0) ? 1u : 0u;
    }

    private static uint InvalidInteger(uint sign, uint width, uint signed, uint word, uint nan)
    {
        if (word == 2)
        {
            return OverflowFault;
        }

        if (nan != 0 || (signed == 0 && sign != 0))
        {
            return 0;
        }

        if (signed == 0)
        {
            return 0xFFFFFFFFu;
        }

        if (width == 32)
        {
            return sign == 0 ? 0x7FFFFFFFu : 0x80000000u;
        }

        return word == 0 ? (sign == 0 ? 0xFFFFFFFFu : 0) : (sign == 0 ? 0x7FFFFFFFu : 0x80000000u);
    }

    private static uint RoundSingle(uint significand, uint exponent, uint sign)
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

        return exponent >= 0xFFu ? sign << 31 | 0x7F800000u : sign << 31 | exponent << 23 | significand & 0x7FFFFFu;
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
