namespace WarpCLR.Compiler;

internal static partial class WarpPortableBinary64Intrinsics
{
    public static uint Sign(uint low, uint high)
    {
        if (((high & 0x7FFFFFFFu) | low) == 0 || SignFault(low, high) != 0)
        {
            return 0;
        }

        return high >> 31 != 0 ? 0xFFFFFFFFu : 1;
    }

    public static uint SignFault(uint low, uint high) => IsNaN(low, high) != 0 ? WarpPortableMathFault.Arithmetic : WarpPortableMathFault.None;

    // These representation operations preserve payload bits, including signaling NaNs.
    public static uint CopySignLow(uint low, uint high, uint signLow, uint signHigh) => low;

    public static uint CopySignHigh(uint low, uint high, uint signLow, uint signHigh) =>
        high & 0x7FFFFFFFu | signHigh & 0x80000000u;

    public static uint BitIncrementLow(uint low, uint high) => BitStep(low, high, 0, 0);

    public static uint BitIncrementHigh(uint low, uint high) => BitStep(low, high, 0, 1);

    public static uint BitDecrementLow(uint low, uint high) => BitStep(low, high, 1, 0);

    public static uint BitDecrementHigh(uint low, uint high) => BitStep(low, high, 1, 1);

    public static uint ILogB(uint low, uint high)
    {
        uint exponent = high >> 20 & 0x7FFu;
        if (exponent == 0x7FFu)
        {
            return 0x7FFFFFFFu;
        }

        if (((high & 0x7FFFFFFFu) | low) == 0)
        {
            return 0x80000000u;
        }

        high &= 0xFFFFFu;
        if (exponent == 0)
        {
            exponent = 4097;
            while ((high & 0x100000u) == 0)
            {
                high = high << 1 | low >> 31;
                low <<= 1;
                exponent--;
            }

            return unchecked(exponent - 5119);
        }

        return unchecked(exponent - 1023);
    }

    private static uint BitStep(uint low, uint high, uint decrement, uint word)
    {
        if (IsNaN(low, high) != 0 || (low == 0 && high == (decrement == 0 ? 0x7FF00000u : 0xFFF00000u)))
        {
            return word == 0 ? low : high;
        }

        if (((high & 0x7FFFFFFFu) | low) == 0)
        {
            return word == 0 ? 1u : decrement << 31;
        }

        if ((high >> 31 ^ decrement) == 0)
        {
            low = unchecked(low + 1);
            high += low == 0 ? 1u : 0u;
        }
        else
        {
            high -= low == 0 ? 1u : 0u;
            low = unchecked(low - 1);
        }

        return word == 0 ? low : high;
    }
}
