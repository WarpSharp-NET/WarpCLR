namespace WarpCLR.Tests;

internal sealed partial class WarpPortablePrimitiveFormatTests
{
    private static IEnumerable<uint> Floating32Witnesses()
    {
        uint[] directed = [0, 0x80000000, 1, 0x80000001, 0x007FFFFF, 0x00800000, 0x00800001, 0x3DCCCCCD,
            0x3F800000, 0xBF800000, 0x4B189680, 0x7F7FFFFF, 0xFF7FFFFF, 0x7F800000, 0xFF800000,
            0x7FC00000, 0xFFC00000, 0x7FA12345, 0xFFA12345];
        foreach (uint bits in directed) { yield return bits; }
        for (uint exponent = 1; exponent < 255; exponent++)
        {
            uint bits = exponent << 23;
            yield return bits - 1; yield return bits; yield return bits + 1;
            yield return bits | 0x80000000;
        }
        for (int exponent = -44; exponent <= 38; exponent++)
        {
            uint bits = BitConverter.SingleToUInt32Bits(float.Parse("1E" + exponent.ToString(System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture));
            yield return bits - 1; yield return bits; yield return bits + 1;
        }
        uint random = 0x6A19C857;
        for (int index = 0; index < 4096; index++)
        {
            random ^= random << 13; random ^= random >> 17; random ^= random << 5;
            yield return random;
        }
    }

    private static IEnumerable<ulong> Floating64Witnesses()
    {
        ulong[] directed = [0, 0x8000000000000000, 1, 0x8000000000000001, 0x000FFFFFFFFFFFFF,
            0x0010000000000000, 0x0010000000000001, 0x3FB999999999999A, 0x3FF0000000000000,
            0xBFF0000000000000, 0x4341C37937E08000, 0x7FEFFFFFFFFFFFFF, 0xFFEFFFFFFFFFFFFF,
            0x7FF0000000000000, 0xFFF0000000000000, 0x7FF8000000000000, 0xFFF8000000000000, 0x7FF0000000012345];
        foreach (ulong bits in directed) { yield return bits; }
        for (ulong exponent = 1; exponent < 2047; exponent++)
        {
            ulong bits = exponent << 52;
            yield return bits - 1; yield return bits; yield return bits + 1;
            yield return bits | 0x8000000000000000;
        }
        for (int exponent = -323; exponent <= 308; exponent++)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(double.Parse("1E" + exponent.ToString(System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture));
            yield return bits - 1; yield return bits; yield return bits + 1;
        }
        ulong random = 0x5819C7514DA39B61;
        for (int index = 0; index < 4096; index++)
        {
            random ^= random << 13; random ^= random >> 7; random ^= random << 17;
            yield return random;
        }
    }

    private static IEnumerable<(uint Kind, ulong Bits)> GeneratedWitnesses()
    {
        yield return (8, 0); yield return (8, 0x80000000); yield return (8, 1);
        yield return (8, 0x00800000); yield return (8, 0x3DCCCCCD); yield return (8, 0x7F7FFFFF);
        yield return (8, 0x7FA12345); yield return (8, 0xFF800000);
        yield return (9, 0); yield return (9, 0x8000000000000000); yield return (9, 1);
        yield return (9, 0x0010000000000000); yield return (9, 0x3FB999999999999A);
        yield return (9, 0x7FEFFFFFFFFFFFFF); yield return (9, 0x7FF0000000012345);
        yield return (9, 0xFFF0000000000000); yield return (6, 0x8000000000000000);
        yield return (7, ulong.MaxValue); yield return (0, 0x80); yield return (10, 0xD800);
        yield return (11, 0); yield return (11, 1);
    }
}
