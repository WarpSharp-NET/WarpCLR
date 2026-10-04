using System.Globalization;
using System.Numerics;
using System.Reflection;
using WarpCLR.Compiler;

namespace WarpCLR.Tests.Production;

internal static class WarpPortableTranscendentalOracle
{
    private static readonly BigInteger TwoOverPi = BigInteger.Parse("0a2f9836e4e441529fc2757d1f534ddc0db6295993c439041fe5163abdebbc561b7246e3a424dd2e006492eea09d1921cfe1deb1cb129a73ee88235f52ebb4484e99c7026b45f7e413991d639835339f49c845f8bbdf9283b1ff897ffde05980fef2f118b5a0a6d1f6d367ecf27cb09b74f463f669e5fea2d7527bac7ebe5f17b3d0739f78a5292ea6bfb5fb11f8d5d0856033046fc7b6babf0cfbc209af4361da9e391615ee61b086599855f14a068408dffd8804d73273106061556ca73a8c960e27bc08c6b47c419c367cddce8092a8359c4768b961ca6ddaf44d15719053ea5ff07053f7e33e832c2de4f98327dbbc33d26ef6b1e5ef89f3a1f35caf27f1d", NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

    public static ulong Actual(string name, ulong left, ulong right, int width)
    {
        Type type = width == 32 ? typeof(WarpPortableBinary32Transcendentals) : typeof(WarpPortableBinary64Transcendentals);
        MethodInfo low = type.GetMethod(name + (width == 32 ? string.Empty : "Low"))!;
        bool binary = low.GetParameters().Length == (width == 32 ? 2 : 4);
        object[] arguments = width == 32 ? binary ? [checked((uint)left), checked((uint)right)] : [checked((uint)left)] :
            binary ? [unchecked((uint)left), (uint)(left >> 32), unchecked((uint)right), (uint)(right >> 32)] : [unchecked((uint)left), (uint)(left >> 32)];
        uint resultLow = (uint)low.Invoke(null, arguments)!;
        return width == 32 ? resultLow : (ulong)(uint)type.GetMethod(name + "High")!.Invoke(null, arguments)! << 32 | resultLow;
    }

    public static ulong Distance(ulong left, ulong right, int width)
    {
        ulong sign = width == 32 ? 0x80000000UL : 0x8000000000000000UL;
        ulong mask = width == 32 ? 0xFFFFFFFFUL : ulong.MaxValue;
        left = (left & sign) != 0 ? ~left & mask : left ^ sign;
        right = (right & sign) != 0 ? ~right & mask : right ^ sign;
        return left >= right ? left - right : right - left;
    }

    public static ulong Reduction(ulong value, int word)
    {
        int point = 3123 - checked((int)(value >> 52));
        BigInteger product = ((value & 0xFFFFFFFFFFFFFUL) | 0x10000000000000UL) * TwoOverPi;
        BigInteger integer = (product + (BigInteger.One << (point - 1))) >> point;
        if (word == 2) { return checked((ulong)(integer & 3)); }
        BigInteger fraction = (product & ((BigInteger.One << point) - 1)) >> (point - 192);
        bool negative = fraction >= BigInteger.One << 191;
        if (negative) { fraction = (BigInteger.One << 192) - fraction; }
        ulong rounded = Round(fraction, -192, negative);
        (BigInteger mantissa, int exponent) = Decode(rounded);
        (BigInteger piMantissa, int piExponent) = Decode(0x3FF921FB54442D18);
        ulong result = Round(mantissa * piMantissa, exponent + piExponent, negative: false);
        return word == 0 ? unchecked((uint)result) : result >> 32;
    }

    private static ulong Round(BigInteger exact, int exponent, bool negative)
    {
        if (exact.Sign < 0) { negative = !negative; exact = -exact; }
        int leading = checked((int)exact.GetBitLength()) - 1;
        int shift = leading - 52;
        BigInteger rounded;
        if (shift <= 0) { rounded = exact << -shift; }
        else
        {
            rounded = exact >> shift;
            BigInteger remainder = exact - (rounded << shift);
            BigInteger half = BigInteger.One << (shift - 1);
            if (remainder > half || (remainder == half && !rounded.IsEven)) { rounded++; }
        }

        if (rounded >= BigInteger.One << 53) { rounded >>= 1; leading++; }
        return (negative ? 0x8000000000000000UL : 0) | checked((ulong)(leading + exponent + 1023)) << 52 | (checked((ulong)rounded) & 0xFFFFFFFFFFFFFUL);
    }

    private static (BigInteger Mantissa, int Exponent) Decode(ulong value)
    {
        BigInteger mantissa = (value & 0xFFFFFFFFFFFFFUL) | 0x10000000000000UL;
        if ((value >> 63) != 0) { mantissa = -mantissa; }
        return (mantissa, checked((int)(value >> 52 & 2047)) - 1075);
    }
}
