namespace WarpCLR.Compiler;

internal static partial class WarpPortableBinary64Intrinsics
{
    private static uint PowerOfTen(uint digits, uint word) =>
        digits < 8 ? PowerOfTenLowDigits(digits, word) : PowerOfTenHighDigits(digits, word);

    private static uint PowerOfTenLowDigits(uint digits, uint word)
    {
        if (digits == 0)
        {
            return word == 0 ? 0x00000000u : 0x3FF00000u;
        }

        if (digits == 1)
        {
            return word == 0 ? 0x00000000u : 0x40240000u;
        }

        if (digits == 2)
        {
            return word == 0 ? 0x00000000u : 0x40590000u;
        }

        if (digits == 3)
        {
            return word == 0 ? 0x00000000u : 0x408F4000u;
        }

        if (digits == 4)
        {
            return word == 0 ? 0x00000000u : 0x40C38800u;
        }

        if (digits == 5)
        {
            return word == 0 ? 0x00000000u : 0x40F86A00u;
        }

        if (digits == 6)
        {
            return word == 0 ? 0x00000000u : 0x412E8480u;
        }

        if (digits == 7)
        {
            return word == 0 ? 0x00000000u : 0x416312D0u;
        }

        return 0;
    }

    private static uint PowerOfTenHighDigits(uint digits, uint word)
    {
        if (digits == 8)
        {
            return word == 0 ? 0x00000000u : 0x4197D784u;
        }

        if (digits == 9)
        {
            return word == 0 ? 0x00000000u : 0x41CDCD65u;
        }

        if (digits == 10)
        {
            return word == 0 ? 0x20000000u : 0x4202A05Fu;
        }

        if (digits == 11)
        {
            return word == 0 ? 0xE8000000u : 0x42374876u;
        }

        if (digits == 12)
        {
            return word == 0 ? 0xA2000000u : 0x426D1A94u;
        }

        if (digits == 13)
        {
            return word == 0 ? 0xE5400000u : 0x42A2309Cu;
        }

        if (digits == 14)
        {
            return word == 0 ? 0x1E900000u : 0x42D6BCC4u;
        }

        return word == 0 ? 0x26340000u : 0x430C6BF5u;
    }
}
