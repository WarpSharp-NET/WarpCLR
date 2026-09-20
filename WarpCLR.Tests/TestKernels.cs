namespace WarpCLR.Tests;

internal static class TestKernels
{
    public static uint ManifestMap(uint input, uint scalar) => (input * 33u) + scalar;

    public static uint ManifestReduction(uint input, uint scalar) => (input * 33u) + scalar;

    public static uint Grayscale(uint rgba) =>
        (rgba & 0xFF000000u) |
        (((
            ((rgba & 0x000000FFu) * 77u) +
            (((rgba >> 8) & 0x000000FFu) * 150u) +
            (((rgba >> 16) & 0x000000FFu) * 29u)) >> 8) * 0x00010101u);

    public static uint Combine(uint left, uint right, uint mask, uint shift) =>
        ((left + (right * 17u)) ^ mask) << (int)shift;

    public static uint Scramble(uint value, uint shift) =>
        ~((value - 0xDEADBEEFu) >> (int)shift) | (value << 3);

    public static uint Divide(uint value, uint divisor) => value / divisor;

    public static uint Branch(uint value) => value == 0 ? 1u : 2u;

    public static uint CompareAndSelect(uint value, uint threshold) =>
        value < threshold
            ? value + 1u
            : value == threshold
                ? 0xA5A5A5A5u
                : value - 1u;

    public static uint Loop(uint value)
    {
        uint result = 0;
        while (value != 0)
        {
            result += value;
            value--;
        }

        return result;
    }

    public static uint Call(uint value) => Rotate(value);

    public static uint WrongParameter(int value) => unchecked((uint)value);

    public static float FloatingPoint(float value) => value + 1.0f;

    private static uint Rotate(uint value) => (value << 1) | (value >> 31);
}
