using System.Numerics;

namespace WarpCLR.Tests.Production;

internal static class WarpPortableMathOracle
{
    public static ulong FusedMultiplyAdd(ulong left, ulong right, ulong addend, int width)
    {
        if (IsNaN(left, width) || IsNaN(right, width) || IsNaN(addend, width))
        {
            return NaN(width);
        }

        bool negativeProduct = IsNegative(left, width) != IsNegative(right, width);
        bool productInfinite = IsInfinite(left, width) || IsInfinite(right, width);
        if (productInfinite)
        {
            return IsZero(left, width) || IsZero(right, width) ||
                (IsInfinite(addend, width) && negativeProduct != IsNegative(addend, width))
                ? NaN(width) : Infinity(width) | (negativeProduct ? SignMask(width) : 0);
        }

        if (IsInfinite(addend, width))
        {
            return addend;
        }

        (BigInteger leftSignificand, int leftExponent) = Decode(left, width);
        (BigInteger rightSignificand, int rightExponent) = Decode(right, width);
        (BigInteger addendSignificand, int addendExponent) = Decode(addend, width);
        int productExponent = leftExponent + rightExponent;
        int exponent = Math.Min(productExponent, addendExponent);
        BigInteger exact = (leftSignificand * rightSignificand << (productExponent - exponent)) +
            (addendSignificand << (addendExponent - exponent));
        bool negativeZero = leftSignificand * rightSignificand == 0 && addendSignificand == 0 &&
            negativeProduct && IsNegative(addend, width);
        return Round(exact, exponent, width, negativeZero);
    }

    public static ulong Remainder(ulong left, ulong right, int width, bool nearest)
    {
        if (IsNaN(left, width) || IsNaN(right, width) || IsInfinite(left, width) || IsZero(right, width))
        {
            return NaN(width);
        }

        if (IsZero(left, width) || IsInfinite(right, width))
        {
            return left;
        }

        (BigInteger leftSignificand, int leftExponent) = Decode(left, width);
        (BigInteger rightSignificand, int rightExponent) = Decode(right, width);
        int exponent = Math.Min(leftExponent, rightExponent);
        BigInteger numerator = BigInteger.Abs(leftSignificand) << (leftExponent - exponent);
        BigInteger denominator = BigInteger.Abs(rightSignificand) << (rightExponent - exponent);
        BigInteger quotient = BigInteger.DivRem(numerator, denominator, out BigInteger remainder);
        if (nearest && (remainder * 2 > denominator || (remainder * 2 == denominator && !quotient.IsEven)))
        {
            remainder -= denominator;
        }

        if (IsNegative(left, width))
        {
            remainder = -remainder;
        }

        return Round(remainder, exponent, width, IsNegative(left, width));
    }

    public static ulong Sqrt(ulong value, int width)
    {
        if (IsNaN(value, width) || (IsNegative(value, width) && !IsZero(value, width)))
        {
            return NaN(width);
        }

        if (IsZero(value, width) || IsInfinite(value, width))
        {
            return value;
        }

        (BigInteger significand, int exponent) = Decode(value, width);
        int leadingExponent = checked((int)significand.GetBitLength()) - 1 + exponent;
        int unitExponent = (leadingExponent >> 1) - FractionBits(width);
        int distance = exponent - 2 * unitExponent;
        BigInteger numerator = distance >= 0 ? significand << distance : significand;
        BigInteger denominator = distance >= 0 ? BigInteger.One : BigInteger.One << -distance;
        BigInteger root = IntegerSqrt(numerator / denominator);
        BigInteger threshold = denominator * ((root * root + root) * 4 + 1);
        if (numerator * 4 > threshold || (numerator * 4 == threshold && !root.IsEven))
        {
            root++;
        }

        return Round(root, unitExponent, width, negativeZero: false);
    }

    private static BigInteger IntegerSqrt(BigInteger value)
    {
        BigInteger estimate = BigInteger.One << checked((int)((value.GetBitLength() + 1) / 2));
        while (true)
        {
            BigInteger next = (estimate + value / estimate) >> 1;
            if (next >= estimate)
            {
                return estimate;
            }

            estimate = next;
        }
    }

    private static ulong Round(BigInteger exact, int exponent, int width, bool negativeZero)
    {
        if (exact == 0)
        {
            return negativeZero ? SignMask(width) : 0;
        }

        ulong sign = exact.Sign < 0 ? SignMask(width) : 0;
        exact = BigInteger.Abs(exact);
        int precision = FractionBits(width) + 1;
        int leadingExponent = checked((int)exact.GetBitLength()) - 1 + exponent;
        int unitExponent = Math.Max(leadingExponent - precision + 1, 2 - Bias(width) - precision);
        int distance = unitExponent - exponent;
        BigInteger rounded = RoundInteger(exact, distance);
        if (rounded >= BigInteger.One << precision)
        {
            rounded >>= 1;
            unitExponent++;
        }

        leadingExponent = unitExponent + precision - 1;
        if (leadingExponent > Bias(width))
        {
            return sign | Infinity(width);
        }

        BigInteger hidden = BigInteger.One << (precision - 1);
        if (rounded < hidden)
        {
            return sign | checked((ulong)rounded);
        }

        return sign | checked((ulong)(leadingExponent + Bias(width))) << (precision - 1) |
            (checked((ulong)rounded) & FractionMask(width));
    }

    private static BigInteger RoundInteger(BigInteger value, int distance)
    {
        if (distance <= 0)
        {
            return value << -distance;
        }

        BigInteger quotient = value >> distance;
        BigInteger remainder = value - (quotient << distance);
        BigInteger halfway = BigInteger.One << (distance - 1);
        return remainder > halfway || (remainder == halfway && !quotient.IsEven) ? quotient + 1 : quotient;
    }

    private static (BigInteger Significand, int Exponent) Decode(ulong value, int width)
    {
        int fractionBits = FractionBits(width);
        int encodedExponent = checked((int)(value >> fractionBits & ExponentMask(width)));
        ulong significand = value & FractionMask(width);
        if (encodedExponent != 0)
        {
            significand |= 1UL << fractionBits;
        }

        BigInteger signed = IsNegative(value, width) ? -(BigInteger)significand : significand;
        return (signed, Math.Max(encodedExponent, 1) - Bias(width) - fractionBits);
    }

    private static int FractionBits(int width) => width == 32 ? 23 : 52;

    private static int Bias(int width) => width == 32 ? 127 : 1023;

    private static ulong SignMask(int width) => width == 32 ? 0x80000000UL : 0x8000000000000000UL;

    private static ulong FractionMask(int width) => width == 32 ? 0x7FFFFFUL : 0xFFFFFFFFFFFFFUL;

    private static ulong ExponentMask(int width) => width == 32 ? 0xFFUL : 0x7FFUL;

    private static ulong Infinity(int width) => width == 32 ? 0x7F800000UL : 0x7FF0000000000000UL;

    private static ulong NaN(int width) => width == 32 ? 0x7FC00000UL : 0x7FF8000000000000UL;

    private static bool IsNegative(ulong value, int width) => (value & SignMask(width)) != 0;

    private static bool IsZero(ulong value, int width) => (value & ~SignMask(width)) == 0;

    private static bool IsInfinite(ulong value, int width) => (value & Infinity(width)) == Infinity(width);

    private static bool IsNaN(ulong value, int width) => IsInfinite(value, width) && (value & FractionMask(width)) != 0;
}
