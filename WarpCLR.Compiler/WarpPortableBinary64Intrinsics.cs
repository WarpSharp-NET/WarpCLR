namespace WarpCLR.Compiler;

internal static partial class WarpPortableBinary64Intrinsics
{
    internal const string Semantics = "warp.math.binary64.integral-scale-representation/rne-gradual-five-round-modes-raw-step-copysign/0.1";

    public static uint FloorLow(uint low, uint high) => RoundLow(low, high, 3);

    public static uint FloorHigh(uint low, uint high) => RoundHigh(low, high, 3);

    public static uint CeilingLow(uint low, uint high) => RoundLow(low, high, 4);

    public static uint CeilingHigh(uint low, uint high) => RoundHigh(low, high, 4);

    public static uint TruncateLow(uint low, uint high) => RoundLow(low, high, 2);

    public static uint TruncateHigh(uint low, uint high) => RoundHigh(low, high, 2);

    public static uint RoundToEvenLow(uint low, uint high) => RoundLow(low, high, 0);

    public static uint RoundToEvenHigh(uint low, uint high) => RoundHigh(low, high, 0);

    public static uint RoundLow(uint low, uint high, uint mode) => Round(low, high, mode, 0);

    public static uint RoundHigh(uint low, uint high, uint mode) => Round(low, high, mode, 1);

    public static uint RoundFault(uint mode) => mode > 4 ? WarpPortableMathFault.Argument : WarpPortableMathFault.None;

    public static uint RoundDigitsFault(uint digits, uint mode) =>
        digits > 15 ? WarpPortableMathFault.ArgumentOutOfRange : RoundFault(mode);

    public static uint RoundDigitsLow(uint low, uint high, uint digits, uint mode) => RoundDigits(low, high, digits, mode, 0);

    public static uint RoundDigitsHigh(uint low, uint high, uint digits, uint mode) => RoundDigits(low, high, digits, mode, 1);

    private static uint Round(uint low, uint high, uint mode, uint word)
    {
        uint exponent = high >> 20 & 0x7FFu;
        if (RoundFault(mode) != 0 || (exponent == 0x7FFu && ((high & 0xFFFFFu) | low) != 0))
        {
            return word == 0 ? 0 : 0x7FF80000u;
        }

        if (exponent >= 1075 || ((high & 0x7FFFFFFFu) | low) == 0)
        {
            return word == 0 ? low : high;
        }

        if (exponent < 1023)
        {
            return RoundBelowOne(low, high, mode, word);
        }

        uint distance = 1075 - exponent;
        uint lowMask = distance >= 32 ? 0xFFFFFFFFu : (1u << (int)distance) - 1;
        uint highMask = distance > 32 ? (1u << (int)(distance - 32)) - 1 : 0;
        uint fractionLow = low & lowMask;
        uint fractionHigh = high & highMask;
        uint increment = Increment(low, high, fractionLow, fractionHigh, distance, mode);
        low &= ~lowMask;
        high &= ~highMask;
        if (increment != 0)
        {
            uint unitLow = distance < 32 ? 1u << (int)distance : 0;
            uint resultLow = unchecked(low + unitLow);
            high += (distance >= 32 ? 1u << (int)(distance - 32) : 0) + (resultLow < low ? 1u : 0u);
            low = resultLow;
        }

        return word == 0 ? low : high;
    }

    private static uint RoundBelowOne(uint low, uint high, uint mode, uint word)
    {
        uint sign = high >> 31;
        uint magnitudeHigh = high & 0x7FFFFFFFu;
        bool aboveHalf = magnitudeHigh > 0x3FE00000u || (magnitudeHigh == 0x3FE00000u && low != 0);
        bool atLeastHalf = magnitudeHigh >= 0x3FE00000u;
        bool one = (mode == 0 && aboveHalf) || (mode == 1 && atLeastHalf) || (mode == 3 && sign != 0) || (mode == 4 && sign == 0);
        return word == 0 ? 0 : sign << 31 | (one ? 0x3FF00000u : 0);
    }

    private static uint Increment(uint low, uint high, uint fractionLow, uint fractionHigh, uint distance, uint mode)
    {
        if ((fractionLow | fractionHigh) == 0 || mode == 2)
        {
            return 0;
        }

        if (mode >= 3)
        {
            return (mode == 3 ? high >> 31 != 0 : high >> 31 == 0) ? 1u : 0u;
        }

        uint halfwayLow = distance <= 32 ? 1u << (int)(distance - 1) : 0;
        uint halfwayHigh = distance > 32 ? 1u << (int)(distance - 33) : 0;
        bool aboveHalf = fractionHigh > halfwayHigh || (fractionHigh == halfwayHigh && fractionLow > halfwayLow);
        bool half = fractionHigh == halfwayHigh && fractionLow == halfwayLow;
        uint odd = distance < 32 ? low >> (int)distance & 1u : high >> (int)(distance - 32) & 1u;
        return aboveHalf || (half && (mode == 1 || odd != 0)) ? 1u : 0u;
    }

    private static uint RoundDigits(uint low, uint high, uint digits, uint mode, uint word)
    {
        if (RoundDigitsFault(digits, mode) != 0 || IsNaN(low, high) != 0)
        {
            return word == 0 ? 0 : 0x7FF80000u;
        }

        uint absoluteHigh = high & 0x7FFFFFFFu;
        if (absoluteHigh > 0x4341C379u || (absoluteHigh == 0x4341C379u && low >= 0x37E08000u))
        {
            return word == 0 ? low : high;
        }

        uint powerLow = PowerOfTen(digits, 0);
        uint powerHigh = PowerOfTen(digits, 1);
        uint scaledLow = WarpPortableBinary64.MultiplyLow(low, high, powerLow, powerHigh);
        uint scaledHigh = WarpPortableBinary64.MultiplyHigh(low, high, powerLow, powerHigh);
        uint roundedLow = RoundLow(scaledLow, scaledHigh, mode);
        uint roundedHigh = RoundHigh(scaledLow, scaledHigh, mode);
        return word == 0 ? WarpPortableBinary64.DivideLow(roundedLow, roundedHigh, powerLow, powerHigh) :
            WarpPortableBinary64.DivideHigh(roundedLow, roundedHigh, powerLow, powerHigh);
    }

    private static uint IsNaN(uint low, uint high) =>
        (high & 0x7FF00000u) == 0x7FF00000u && ((high & 0xFFFFFu) | low) != 0 ? 1u : 0u;
}
