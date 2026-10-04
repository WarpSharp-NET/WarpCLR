using System.Numerics;

namespace WarpCLR.Tests.Production;

internal static class WarpPortableIntrinsicOracle
{
    public static ulong Integral(ulong value, int width, uint mode)
    {
        if (mode > 4 || IsNaN(value, width)) { return NaN(width); }
        if (IsInfinite(value, width) || IsZero(value, width)) { return value; }
        (BigInteger significand, int exponent) = Decode(value, width);
        if (exponent >= 0) { return value; }
        bool negative = significand.Sign < 0;
        BigInteger magnitude = BigInteger.Abs(significand);
        int distance = -exponent;
        BigInteger integer = magnitude >> distance;
        BigInteger fraction = magnitude - (integer << distance);
        BigInteger half = BigInteger.One << (distance - 1);
        bool increment = mode == 0 ? fraction > half || (fraction == half && !integer.IsEven) :
            mode == 1 ? fraction >= half : mode == 3 ? negative && fraction != 0 : mode == 4 && !negative && fraction != 0;
        if (increment) { integer++; }
        return RoundDyadic(negative ? -integer : integer, 0, width, negative);
    }

    public static ulong Digits(ulong value, int width, uint digits, uint mode)
    {
        if (digits > (width == 32 ? 6 : 15) || mode > 4 || IsNaN(value, width)) { return NaN(width); }
        ulong limit = width == 32 ? 0x4CBEBC20UL : 0x4341C37937E08000UL;
        if ((value & ~SignMask(width)) >= limit) { return value; }
        (BigInteger significand, int exponent) = Decode(value, width);
        BigInteger power = BigInteger.Pow(10, checked((int)digits));
        ulong scaled = RoundDyadic(significand * power, exponent, width, IsNegative(value, width));
        ulong integral = Integral(scaled, width, mode);
        (BigInteger rounded, int roundedExponent) = Decode(integral, width);
        return RoundRational(rounded, power, roundedExponent, width, IsNegative(integral, width));
    }

    public static ulong ScaleB(ulong value, int width, uint encodedScale)
    {
        if (IsNaN(value, width)) { return NaN(width); }
        if (IsInfinite(value, width) || IsZero(value, width)) { return value; }
        int scale = unchecked((int)encodedScale);
        ulong sign = IsNegative(value, width) ? SignMask(width) : 0;
        if (scale > 4096) { return sign | Infinity(width); }
        if (scale < -4096) { return sign; }
        (BigInteger significand, int exponent) = Decode(value, width);
        return RoundDyadic(significand, exponent + scale, width, negativeZero: false);
    }

    public static uint ILogB(ulong value, int width)
    {
        if (IsInfinite(value, width)) { return 0x7FFFFFFF; }
        if (IsZero(value, width)) { return 0x80000000; }
        (BigInteger significand, int exponent) = Decode(value, width);
        return unchecked((uint)(checked((int)BigInteger.Abs(significand).GetBitLength()) - 1 + exponent));
    }

    private static ulong RoundRational(BigInteger numerator, BigInteger denominator, int exponent, int width, bool negativeZero)
    {
        if (numerator == 0) { return negativeZero ? SignMask(width) : 0; }
        bool negative = numerator.Sign < 0;
        numerator = BigInteger.Abs(numerator);
        int lead = checked((int)(numerator.GetBitLength() - denominator.GetBitLength()));
        if (lead >= 0 ? numerator < denominator << lead : numerator << -lead < denominator) { lead--; }
        int precision = FractionBits(width) + 1;
        int unit = Math.Max(lead + exponent - precision + 1, 2 - Bias(width) - precision);
        int shift = exponent - unit;
        BigInteger scaledNumerator = shift >= 0 ? numerator << shift : numerator;
        BigInteger scaledDenominator = shift >= 0 ? denominator : denominator << -shift;
        BigInteger quotient = BigInteger.DivRem(scaledNumerator, scaledDenominator, out BigInteger remainder);
        if (remainder * 2 > scaledDenominator || (remainder * 2 == scaledDenominator && !quotient.IsEven)) { quotient++; }
        return RoundDyadic(negative ? -quotient : quotient, unit, width, negative);
    }

    private static ulong RoundDyadic(BigInteger exact, int exponent, int width, bool negativeZero)
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
