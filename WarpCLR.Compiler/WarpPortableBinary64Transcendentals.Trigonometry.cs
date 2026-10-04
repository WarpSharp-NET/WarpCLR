namespace WarpCLR.Compiler;

internal static partial class WarpPortableBinary64Transcendentals
{
    private static uint Trigonometric(uint low, uint high, uint kind, uint word)
    {
        uint sign = high >> 31;
        high &= 0x7FFFFFFFu;
        if (high >= 0x7FF00000u) { return word == 0 ? 0 : 0x7FF80000u; }
        if (high < 0x3E400000u)
        {
            return kind == 1 ? Constant(0, word) : word == 0 ? low : high | sign << 31;
        }

        uint quadrant = 0;
        uint reducedLow = low;
        uint reducedHigh = high;
        if (LessMagnitude(ConstantLow(3), ConstantHigh(3), low, high) != 0)
        {
            quadrant = Reduction(low, high, 2);
            reducedLow = Reduction(low, high, 0);
            reducedHigh = Reduction(low, high, 1);
        }

        if (kind != 2)
        {
            uint result = TrigonometricComponent(reducedLow, reducedHigh, quadrant, kind, word);
            return word != 0 && kind == 0 ? result ^ sign << 31 : result;
        }

        uint sineLow = TrigonometricComponent(reducedLow, reducedHigh, quadrant, 0, 0);
        uint sineHigh = TrigonometricComponent(reducedLow, reducedHigh, quadrant, 0, 1);
        uint cosineLow = TrigonometricComponent(reducedLow, reducedHigh, quadrant, 1, 0);
        uint cosineHigh = TrigonometricComponent(reducedLow, reducedHigh, quadrant, 1, 1);
        uint quotient = Divide(sineLow, sineHigh, cosineLow, cosineHigh, word);
        return word == 0 ? quotient : quotient ^ sign << 31;
    }

    private static uint TrigonometricComponent(uint low, uint high, uint quadrant, uint kind, uint word)
    {
        uint polynomialKind = (kind == 0 ? 1u : 2u) ^ (quadrant & 1u) * 3;
        uint squareLow = Multiply(low, high, low, high, 0);
        uint squareHigh = Multiply(low, high, low, high, 1);
        uint resultLow = Polynomial(squareLow, squareHigh, polynomialKind, 12, 0);
        uint resultHigh = Polynomial(squareLow, squareHigh, polynomialKind, 12, 1);
        if (polynomialKind == 1)
        {
            uint nextLow = Multiply(resultLow, resultHigh, low, high, 0);
            resultHigh = Multiply(resultLow, resultHigh, low, high, 1);
            resultLow = nextLow;
        }

        uint negative = kind == 0 ? quadrant >> 1 : (quadrant + 1) >> 1 & 1u;
        return word == 0 ? resultLow : resultHigh ^ negative << 31;
    }
}
