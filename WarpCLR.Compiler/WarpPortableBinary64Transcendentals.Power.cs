namespace WarpCLR.Compiler;

internal static partial class WarpPortableBinary64Transcendentals
{
    private static uint Power(uint low, uint high, uint exponentLow, uint exponentHigh, uint word)
    {
        if (IsZero(exponentLow, exponentHigh) != 0 || (low == 0 && high == 0x3FF00000u)) { return Constant(0, word); }
        if (IsNaN(low, high) != 0 || IsNaN(exponentLow, exponentHigh) != 0) { return word == 0 ? 0 : 0x7FF80000u; }
        uint magnitude = high & 0x7FFFFFFFu;
        uint exponentMagnitude = exponentHigh & 0x7FFFFFFFu;
        uint exponentNegative = exponentHigh >> 31;
        if (exponentMagnitude == 0x7FF00000u)
        {
            if (low == 0 && magnitude == 0x3FF00000u) { return Constant(0, word); }
            uint greater = LessMagnitude(0, 0x3FF00000u, low, magnitude);
            return word == 0 ? 0 : (greater ^ exponentNegative) != 0 ? 0x7FF00000u : 0;
        }

        uint integer = IsIntegral(exponentLow, exponentMagnitude);
        uint odd = integer == 0 || exponentMagnitude >= 0x43400000u ? 0 :
            WarpPortableNumericConversions.DoubleToUInt64Low(exponentLow, exponentMagnitude) & 1u;
        uint negative = high >> 31 & odd;
        if (IsZero(low, high) != 0 || magnitude == 0x7FF00000u)
        {
            uint infinite = (magnitude == 0x7FF00000u ? 1u : 0u) ^ exponentNegative;
            return word == 0 ? 0 : negative << 31 | (infinite != 0 ? 0x7FF00000u : 0);
        }

        if ((high >> 31) != 0 && integer == 0) { return word == 0 ? 0 : 0x7FF80000u; }
        if (exponentLow == 0 && exponentMagnitude == 0x3FF00000u)
        {
            return exponentNegative == 0 ? word == 0 ? low : high : Divide(0, 0x3FF00000u, low, high, word);
        }

        uint result = PrecisePowerFinite(low, magnitude, exponentLow, exponentHigh, word);
        return word == 0 ? result : result ^ negative << 31;
    }

    private static uint IsIntegral(uint low, uint high) =>
        WarpPortableBinary64Intrinsics.TruncateLow(low, high) == low &&
            WarpPortableBinary64Intrinsics.TruncateHigh(low, high) == high ? 1u : 0u;

    private static uint LogarithmBase(uint low, uint high, uint baseLow, uint baseHigh, uint word)
    {
        if (IsNaN(baseLow, baseHigh) != 0 || (baseLow == 0 && baseHigh == 0x3FF00000u) ||
            ((low != 0 || high != 0x3FF00000u) && (IsZero(baseLow, baseHigh) != 0 || baseHigh == 0x7FF00000u)))
        {
            return word == 0 ? 0 : 0x7FF80000u;
        }

        return Divide(Logarithm(low, high, 0, 0), Logarithm(low, high, 0, 1),
            Logarithm(baseLow, baseHigh, 0, 0), Logarithm(baseLow, baseHigh, 0, 1), word);
    }

    private static uint CubeRoot(uint low, uint high, uint word)
    {
        uint sign = high >> 31;
        high &= 0x7FFFFFFFu;
        if (IsNaN(low, high) != 0) { return word == 0 ? 0 : 0x7FF80000u; }
        if (IsZero(low, high) != 0 || high == 0x7FF00000u) { return word == 0 ? low : high | sign << 31; }
        uint offsetExponent = unchecked(WarpPortableBinary64Intrinsics.ILogB(low, high) + 1200);
        uint quotient = 0;
        while (offsetExponent >= 3) { offsetExponent -= 3; quotient++; }
        quotient = unchecked(quotient - 400);
        uint scaledLow = Scale(low, high, unchecked(0u - 3 * quotient), 0);
        uint scaledHigh = Scale(low, high, unchecked(0u - 3 * quotient), 1);
        uint resultLow = 0;
        uint resultHigh = 0x40000000u;
        for (uint iteration = 0; iteration < 9; iteration++)
        {
            uint denominatorLow = Multiply(resultLow, resultHigh, resultLow, resultHigh, 0);
            uint denominatorHigh = Multiply(resultLow, resultHigh, resultLow, resultHigh, 1);
            uint correctionLow = Divide(scaledLow, scaledHigh, denominatorLow, denominatorHigh, 0);
            uint correctionHigh = Divide(scaledLow, scaledHigh, denominatorLow, denominatorHigh, 1);
            uint sumLow = Add(resultLow, resultHigh + 0x100000u, correctionLow, correctionHigh, 0);
            uint sumHigh = Add(resultLow, resultHigh + 0x100000u, correctionLow, correctionHigh, 1);
            resultLow = Multiply(sumLow, sumHigh, ConstantLow(10), ConstantHigh(10), 0);
            resultHigh = Multiply(sumLow, sumHigh, ConstantLow(10), ConstantHigh(10), 1);
        }

        uint result = Scale(resultLow, resultHigh, quotient, word);
        return word == 0 ? result : result | sign << 31;
    }
}
