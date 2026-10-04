namespace WarpCLR.Compiler;

internal static class WarpPortableBinary32Intrinsics
{
    internal const string Semantics = "warp.math.binary32.integral-scale-representation/rne-gradual-five-round-modes-raw-step-copysign/0.1";

    public static uint Floor(uint value) => Round(value, 3);

    public static uint Ceiling(uint value) => Round(value, 4);

    public static uint Truncate(uint value) => Round(value, 2);

    public static uint RoundToEven(uint value) => Round(value, 0);

    public static uint Round(uint value, uint mode)
    {
        uint low = WarpPortableNumericConversions.SingleToDoubleLow(value);
        uint high = WarpPortableNumericConversions.SingleToDoubleHigh(value);
        return WarpPortableNumericConversions.DoubleToSingle(WarpPortableBinary64Intrinsics.RoundLow(low, high, mode),
            WarpPortableBinary64Intrinsics.RoundHigh(low, high, mode));
    }

    public static uint RoundFault(uint mode) => WarpPortableBinary64Intrinsics.RoundFault(mode);

    public static uint RoundDigitsFault(uint digits, uint mode) =>
        digits > 6 ? WarpPortableMathFault.ArgumentOutOfRange : RoundFault(mode);

    public static uint RoundDigits(uint value, uint digits, uint mode)
    {
        if (RoundDigitsFault(digits, mode) != 0 || SignFault(value) != 0)
        {
            return 0x7FC00000u;
        }

        if ((value & 0x7FFFFFFFu) >= 0x4CBEBC20u)
        {
            return value;
        }

        uint power = PowerOfTen(digits);
        return WarpPortableBinary32.Divide(Round(WarpPortableBinary32.Multiply(value, power), mode), power);
    }

    public static uint Sign(uint value) =>
        (value & 0x7FFFFFFFu) == 0 || SignFault(value) != 0 ? 0 : value >> 31 != 0 ? 0xFFFFFFFFu : 1;

    public static uint SignFault(uint value) =>
        (value & 0x7F800000u) == 0x7F800000u && (value & 0x7FFFFFu) != 0 ? WarpPortableMathFault.Arithmetic : WarpPortableMathFault.None;

    public static uint CopySign(uint value, uint sign) => value & 0x7FFFFFFFu | sign & 0x80000000u;

    public static uint BitIncrement(uint value) => BitStep(value, 0);

    public static uint BitDecrement(uint value) => BitStep(value, 1);

    public static uint ScaleB(uint value, uint scale)
    {
        uint low = WarpPortableNumericConversions.SingleToDoubleLow(value);
        uint high = WarpPortableNumericConversions.SingleToDoubleHigh(value);
        return WarpPortableNumericConversions.DoubleToSingle(WarpPortableBinary64Intrinsics.ScaleBLow(low, high, scale),
            WarpPortableBinary64Intrinsics.ScaleBHigh(low, high, scale));
    }

    public static uint ILogB(uint value) => WarpPortableBinary64Intrinsics.ILogB(
        WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value));

    private static uint BitStep(uint value, uint decrement)
    {
        if (SignFault(value) != 0 || value == (decrement == 0 ? 0x7F800000u : 0xFF800000u))
        {
            return value;
        }

        if ((value & 0x7FFFFFFFu) == 0)
        {
            return decrement << 31 | 1u;
        }

        return (value >> 31 ^ decrement) == 0 ? unchecked(value + 1) : unchecked(value - 1);
    }

    private static uint PowerOfTen(uint digits)
    {
        if (digits == 0)
        {
            return 0x3F800000u;
        }

        if (digits == 1)
        {
            return 0x41200000u;
        }

        if (digits == 2)
        {
            return 0x42C80000u;
        }

        if (digits == 3)
        {
            return 0x447A0000u;
        }

        if (digits == 4)
        {
            return 0x461C4000u;
        }

        if (digits == 5)
        {
            return 0x47C35000u;
        }

        return 0x49742400u;
    }
}
