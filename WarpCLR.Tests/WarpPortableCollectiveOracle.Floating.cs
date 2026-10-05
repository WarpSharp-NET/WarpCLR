using System.Numerics;

namespace WarpCLR.Tests.Production;

internal static partial class WarpPortableCollectiveOracle
{
    private static ulong FloatCombine(uint type, uint operation, ulong left, ulong right)
    {
        bool single = type == 5;
        double a = single ? BitConverter.UInt32BitsToSingle((uint)left) : BitConverter.UInt64BitsToDouble(left);
        double b = single ? BitConverter.UInt32BitsToSingle((uint)right) : BitConverter.UInt64BitsToDouble(right);
        ulong native;
        if (single)
        {
            float x = (float)a, y = (float)b;
            float result = operation == 1 ? x + y : operation == 4 ? x * y : operation == 2 ? MathF.Min(x, y) : MathF.Max(x, y);
            native = float.IsNaN(result) ? 0x7FC00000UL : BitConverter.SingleToUInt32Bits(result);
        }
        else
        {
            double result = operation == 1 ? a + b : operation == 4 ? a * b : operation == 2 ? Math.Min(a, b) : Math.Max(a, b);
            native = double.IsNaN(result) ? 0x7FF8000000000000UL : BitConverter.DoubleToUInt64Bits(result);
        }
        ulong exact = operation == 1 || operation == 4 ? ExactFloat(left, right, single, operation == 4) :
            OrderedFloat(left, right, single, operation == 2);
        Assert.AreEqual(exact, native, $"Independent/native oracle mismatch type={type}, operation={operation}, left={left:X16}, right={right:X16}");
        return exact;
    }

    private static ulong OrderedFloat(ulong left, ulong right, bool single, bool minimum)
    {
        ulong sign = single ? 0x80000000UL : 0x8000000000000000UL;
        ulong infinity = single ? 0x7F800000UL : 0x7FF0000000000000UL;
        ulong canonical = single ? 0x7FC00000UL : 0x7FF8000000000000UL;
        ulong a = left & ~sign, b = right & ~sign;
        if (a > infinity || b > infinity) { return canonical; }
        if ((a | b) == 0) { return minimum ? left | right : left & right; }
        ulong keyA = (left & sign) != 0 ? ~left : left ^ sign;
        ulong keyB = (right & sign) != 0 ? ~right : right ^ sign;
        if (single) { keyA &= uint.MaxValue; keyB &= uint.MaxValue; }
        return minimum ? keyA < keyB ? left : right : keyA > keyB ? left : right;
    }

    private static ulong ExactFloat(ulong left, ulong right, bool single, bool multiply)
    {
        ulong sign = single ? 0x80000000UL : 0x8000000000000000UL;
        ulong infinity = single ? 0x7F800000UL : 0x7FF0000000000000UL;
        ulong canonical = single ? 0x7FC00000UL : 0x7FF8000000000000UL;
        ulong a = left & ~sign, b = right & ~sign;
        if (a > infinity || b > infinity) { return canonical; }
        if (multiply)
        {
            ulong productSign = (left ^ right) & sign;
            if ((a == infinity && b == 0) || (b == infinity && a == 0)) { return canonical; }
            if (a == infinity || b == infinity) { return infinity | productSign; }
            if (a == 0 || b == 0) { return productSign; }
            (BigInteger numeratorA, int exponentA) = Decode(left, single);
            (BigInteger numeratorB, int exponentB) = Decode(right, single);
            return Round(numeratorA * numeratorB, exponentA + exponentB, single);
        }
        if (a == infinity || b == infinity)
        {
            return a == infinity && b == infinity && ((left ^ right) & sign) != 0 ? canonical : a == infinity ? left : right;
        }
        if ((a | b) == 0) { return left & right & sign; }
        (BigInteger x, int ex) = Decode(left, single);
        (BigInteger y, int ey) = Decode(right, single);
        int common = Math.Min(ex, ey);
        return Round((x << (ex - common)) + (y << (ey - common)), common, single);
    }

    private static (BigInteger Numerator, int Exponent) Decode(ulong bits, bool single)
    {
        int fraction = single ? 23 : 52, bias = single ? 127 : 1023;
        ulong sign = single ? 0x80000000UL : 0x8000000000000000UL;
        int exponent = (int)((bits & ~sign) >> fraction);
        BigInteger magnitude = bits & ((1UL << fraction) - 1);
        if (exponent != 0) { magnitude |= BigInteger.One << fraction; }
        return ((bits & sign) != 0 ? -magnitude : magnitude, (exponent == 0 ? 1 : exponent) - bias - fraction);
    }

    private static ulong Round(BigInteger numerator, int exponent, bool single)
    {
        int fraction = single ? 23 : 52, bias = single ? 127 : 1023;
        ulong sign = numerator.Sign < 0 ? single ? 0x80000000UL : 0x8000000000000000UL : 0;
        BigInteger magnitude = BigInteger.Abs(numerator);
        if (magnitude.IsZero) { return sign; }
        int top = checked((int)magnitude.GetBitLength()) - 1 + exponent;
        int quantum = Math.Max(top - fraction, 1 - bias - fraction);
        int shift = quantum - exponent;
        BigInteger rounded;
        if (shift <= 0) { rounded = magnitude << -shift; }
        else
        {
            rounded = magnitude >> shift;
            BigInteger tail = magnitude - (rounded << shift), half = BigInteger.One << (shift - 1);
            if (tail > half || (tail == half && !rounded.IsEven)) { rounded++; }
        }
        if (rounded.IsZero) { return sign; }
        top = checked((int)rounded.GetBitLength()) - 1 + quantum;
        if (top > bias) { return sign | (single ? 0x7F800000UL : 0x7FF0000000000000UL); }
        if (top < 1 - bias) { return sign | (ulong)rounded; }
        int normalization = checked((int)rounded.GetBitLength()) - fraction - 1;
        ulong mantissa = (ulong)(normalization >= 0 ? rounded >> normalization : rounded << -normalization);
        return sign | ((ulong)(top + bias) << fraction) | (mantissa & ((1UL << fraction) - 1));
    }
}
