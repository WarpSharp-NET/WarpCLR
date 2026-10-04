namespace WarpCLR.Compiler;

internal static class WarpPortableInteger32
{
    internal const string Semantics = "warp.integer32/wrap-checked-divrem/0.1";

    public static uint Add(uint left, uint right) => unchecked(left + right);

    public static uint Subtract(uint left, uint right) => unchecked(left - right);

    public static uint Multiply(uint left, uint right) => unchecked(left * right);

    public static uint Negate(uint value) => unchecked(0u - value);

    public static uint LessThanUnsigned(uint left, uint right) => left < right ? 1u : 0u;

    public static uint LessThanSigned(uint left, uint right) => (left ^ 0x80000000u) < (right ^ 0x80000000u) ? 1u : 0u;

    public static uint ShiftLeft(uint value, uint distance) => value << (int)(distance & 31u);

    public static uint ShiftRightUnsigned(uint value, uint distance) => value >> (int)(distance & 31u);

    public static uint ShiftRightSigned(uint value, uint distance)
    {
        distance &= 31u;
        if (distance == 0)
        {
            return value;
        }

        uint fill = unchecked(0u - (value >> 31));
        return value >> (int)distance | fill << (int)(32 - distance);
    }

    public static uint DivideUnsigned(uint left, uint right) => DivideRemainder(left, right, 0);

    public static uint RemainderUnsigned(uint left, uint right) => DivideRemainder(left, right, 1);

    public static uint DivideSigned(uint left, uint right)
    {
        if (DivideSignedFault(left, right) != WarpPortableIntegerFault.None)
        {
            return 0;
        }

        uint magnitudeLeft = (left >> 31) != 0 ? Negate(left) : left;
        uint magnitudeRight = (right >> 31) != 0 ? Negate(right) : right;
        uint quotient = DivideUnsigned(magnitudeLeft, magnitudeRight);
        return ((left ^ right) >> 31) != 0 ? Negate(quotient) : quotient;
    }

    public static uint RemainderSigned(uint left, uint right)
    {
        if (DivideSignedFault(left, right) != WarpPortableIntegerFault.None)
        {
            return 0;
        }

        uint magnitudeLeft = (left >> 31) != 0 ? Negate(left) : left;
        uint magnitudeRight = (right >> 31) != 0 ? Negate(right) : right;
        uint remainder = RemainderUnsigned(magnitudeLeft, magnitudeRight);
        return (left >> 31) != 0 ? Negate(remainder) : remainder;
    }

    public static uint DivideUnsignedFault(uint left, uint right) =>
        right == 0 ? WarpPortableIntegerFault.DivideByZero : WarpPortableIntegerFault.None;

    public static uint DivideSignedFault(uint left, uint right) =>
        right == 0 ? WarpPortableIntegerFault.DivideByZero :
        left == 0x80000000u && right == 0xFFFFFFFFu ? WarpPortableIntegerFault.Overflow : WarpPortableIntegerFault.None;

    public static uint AddUnsignedFault(uint left, uint right) =>
        Add(left, right) < left ? WarpPortableIntegerFault.Overflow : WarpPortableIntegerFault.None;

    public static uint AddSignedFault(uint left, uint right) =>
        (((left ^ Add(left, right)) & (right ^ Add(left, right))) >> 31) != 0 ?
            WarpPortableIntegerFault.Overflow : WarpPortableIntegerFault.None;

    public static uint SubtractUnsignedFault(uint left, uint right) =>
        left < right ? WarpPortableIntegerFault.Overflow : WarpPortableIntegerFault.None;

    public static uint SubtractSignedFault(uint left, uint right) =>
        (((left ^ right) & (left ^ Subtract(left, right))) >> 31) != 0 ?
            WarpPortableIntegerFault.Overflow : WarpPortableIntegerFault.None;

    public static uint MultiplyUnsignedFault(uint left, uint right) =>
        WarpPortableWordMath.MultiplyHigh(left, right) != 0 ? WarpPortableIntegerFault.Overflow : WarpPortableIntegerFault.None;

    public static uint MultiplySignedFault(uint left, uint right)
    {
        uint magnitudeLeft = (left >> 31) != 0 ? Negate(left) : left;
        uint magnitudeRight = (right >> 31) != 0 ? Negate(right) : right;
        uint maximum = ((left ^ right) >> 31) != 0 ? 0x80000000u : 0x7FFFFFFFu;
        return WarpPortableWordMath.MultiplyHigh(magnitudeLeft, magnitudeRight) != 0 ||
            Multiply(magnitudeLeft, magnitudeRight) > maximum ? WarpPortableIntegerFault.Overflow : WarpPortableIntegerFault.None;
    }

    public static uint NegateSignedFault(uint value) =>
        value == 0x80000000u ? WarpPortableIntegerFault.Overflow : WarpPortableIntegerFault.None;

    public static uint ExtendUnsigned8(uint value) => value & 0xFFu;

    public static uint ExtendUnsigned16(uint value) => value & 0xFFFFu;

    public static uint ExtendSigned8(uint value) =>
        value & 0xFFu | ((value & 0x80u) != 0 ? 0xFFFFFF00u : 0u);

    public static uint ExtendSigned16(uint value) =>
        value & 0xFFFFu | ((value & 0x8000u) != 0 ? 0xFFFF0000u : 0u);

    public static uint ExtendSignedHigh(uint value) => unchecked(0u - (value >> 31));

    public static uint ConvertToSigned8Fault(uint value, uint sourceUnsigned) =>
        value <= 0x7Fu || (sourceUnsigned == 0 && value >= 0xFFFFFF80u) ?
            WarpPortableIntegerFault.None : WarpPortableIntegerFault.Overflow;

    public static uint ConvertToSigned16Fault(uint value, uint sourceUnsigned) =>
        value <= 0x7FFFu || (sourceUnsigned == 0 && value >= 0xFFFF8000u) ?
            WarpPortableIntegerFault.None : WarpPortableIntegerFault.Overflow;

    public static uint ConvertToUnsigned8Fault(uint value) =>
        value <= 0xFFu ? WarpPortableIntegerFault.None : WarpPortableIntegerFault.Overflow;

    public static uint ConvertToUnsigned16Fault(uint value) =>
        value <= 0xFFFFu ? WarpPortableIntegerFault.None : WarpPortableIntegerFault.Overflow;

    public static uint ConvertUnsignedToSignedFault(uint value) =>
        (value >> 31) != 0 ? WarpPortableIntegerFault.Overflow : WarpPortableIntegerFault.None;

    public static uint ConvertSignedToUnsignedFault(uint value) => ConvertUnsignedToSignedFault(value);

    private static uint DivideRemainder(uint left, uint right, uint selectRemainder)
    {
        if (right == 0)
        {
            return 0;
        }

        uint quotient = 0;
        uint remainder = 0;
        for (uint bit = 0; bit < 32; bit++)
        {
            uint carry = remainder >> 31;
            remainder = remainder << 1 | left >> 31;
            left <<= 1;
            quotient <<= 1;
            if (carry != 0 || remainder >= right)
            {
                remainder = unchecked(remainder - right);
                quotient |= 1;
            }
        }

        return selectRemainder == 0 ? quotient : remainder;
    }
}
