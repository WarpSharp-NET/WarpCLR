using System.Numerics;

namespace WarpCLR.Tests.Production;

// Test-only independent bit permutation; no production service or BCL rotate is evaluated.
internal static class WarpPortableRotateLeftOracle
{
    internal static uint[] Permute(uint[] valueWords, int signedCount)
    {
        int width = checked(valueWords.Length * 32);
        int distance = signedCount % width;
        if (distance < 0) { distance += width; }
        BigInteger value = valueWords.Length == 1 ? valueWords[0] :
            new BigInteger(valueWords[0]) | new BigInteger(valueWords[1]) << 32;
        BigInteger result = BigInteger.Zero;
        for (int bit = 0; bit < width; bit++)
        {
            if ((value & BigInteger.One << bit) != BigInteger.Zero)
            {
                result |= BigInteger.One << ((bit + distance) % width);
            }
        }
        uint low = (uint)(result & uint.MaxValue);
        return valueWords.Length == 1 ? [low] : [low, (uint)(result >> 32)];
    }

    internal static int[] Counts(int width) =>
        [-1, int.MinValue, 0, width, width + 1, int.MaxValue, 1, width - 1, -width, -width - 1, width * 2 + 3, -width * 2 - 3];

    internal static IEnumerable<uint[]> Words(bool wide)
    {
        uint[] narrow = [0, uint.MaxValue, 0x80000001, 0x7FFFFFFF, 0x7F800001, 0xFFC54321, 0x00000001, 0x80000000,
            0x3F800000, 0x007FFFFF, 0x00800000, 0x12345678];
        if (!wide)
        {
            foreach (uint value in narrow) { yield return [value]; }
        }
        else
        {
            yield return [0, 0]; yield return [uint.MaxValue, uint.MaxValue];
            yield return [1, 0x80000000]; yield return [uint.MaxValue, 0x7FFFFFFF];
            yield return [1, 0x7FF00000]; yield return [0x76543210, 0xFFF54321];
            yield return [1, 0]; yield return [0, 0x80000000];
            yield return [0, 0x3FF00000]; yield return [uint.MaxValue, 0x000FFFFF];
            yield return [0, 0x00100000]; yield return [0x12345678, 0x9ABCDEF0];
        }
        int width = wide ? 64 : 32;
        for (int bit = 0; bit < width; bit++)
        {
            yield return !wide ? [1u << bit] : bit < 32 ? [1u << bit, 0] : [0, 1u << (bit - 32)];
        }
    }
}
