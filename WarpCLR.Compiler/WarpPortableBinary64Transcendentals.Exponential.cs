namespace WarpCLR.Compiler;

internal static partial class WarpPortableBinary64Transcendentals
{
    private static uint Exponential(uint low, uint high, uint word) => ExponentialWithTail(low, high, 0, 0, word);

    private static uint ExponentialWithTail(uint low, uint high, uint tailLow, uint tailHigh, uint word)
    {
        uint sign = high >> 31;
        uint magnitude = high & 0x7FFFFFFFu;
        if (IsNaN(low, high) != 0) { return word == 0 ? 0 : 0x7FF80000u; }
        if (magnitude >= 0x40890000u) { return word == 0 ? 0 : sign == 0 ? 0x7FF00000u : 0; }
        uint scaledLow = Multiply(low, high, ConstantLow(6), ConstantHigh(6), 0);
        uint scaledHigh = Multiply(low, high, ConstantLow(6), ConstantHigh(6), 1);
        uint integerLow = WarpPortableBinary64Intrinsics.RoundToEvenLow(scaledLow, scaledHigh);
        uint integerHigh = WarpPortableBinary64Intrinsics.RoundToEvenHigh(scaledLow, scaledHigh);
        uint exponent = WarpPortableNumericConversions.DoubleToInt32(integerLow, integerHigh);
        uint reducedLow = Fused(integerLow, integerHigh ^ 0x80000000u, ConstantLow(4), ConstantHigh(4), low, high, 0);
        uint reducedHigh = Fused(integerLow, integerHigh ^ 0x80000000u, ConstantLow(4), ConstantHigh(4), low, high, 1);
        uint nextLow = Fused(integerLow, integerHigh ^ 0x80000000u, ConstantLow(5), ConstantHigh(5), reducedLow, reducedHigh, 0);
        reducedHigh = Fused(integerLow, integerHigh ^ 0x80000000u, ConstantLow(5), ConstantHigh(5), reducedLow, reducedHigh, 1);
        reducedLow = nextLow;
        uint argumentLow = DoubleDoubleNormalize(reducedLow, reducedHigh, tailLow, tailHigh, 0);
        uint argumentHigh = DoubleDoubleNormalize(reducedLow, reducedHigh, tailLow, tailHigh, 1);
        uint argumentTailLow = DoubleDoubleNormalize(reducedLow, reducedHigh, tailLow, tailHigh, 2);
        uint argumentTailHigh = DoubleDoubleNormalize(reducedLow, reducedHigh, tailLow, tailHigh, 3);
        uint polynomialLow = Polynomial(argumentLow, argumentHigh, 0, 22, 0);
        uint polynomialHigh = Polynomial(argumentLow, argumentHigh, 0, 22, 1);
        return Scale(Fused(polynomialLow, polynomialHigh, argumentTailLow, argumentTailHigh, polynomialLow, polynomialHigh, 0),
            Fused(polynomialLow, polynomialHigh, argumentTailLow, argumentTailHigh, polynomialLow, polynomialHigh, 1), exponent, word);
    }

    private static uint ExponentialMinusOne(uint low, uint high, uint word)
    {
        if ((high & 0x7FFFFFFFu) >= 0x3FE00000u)
        {
            return Subtract(Exponential(low, high, 0), Exponential(low, high, 1), 0, 0x3FF00000u, word);
        }

        uint resultLow = CoefficientLow(0, 22);
        uint resultHigh = CoefficientHigh(0, 22);
        for (uint degree = 21; degree != 0; degree--)
        {
            uint nextLow = Fused(resultLow, resultHigh, low, high, CoefficientLow(0, degree), CoefficientHigh(0, degree), 0);
            resultHigh = Fused(resultLow, resultHigh, low, high, CoefficientLow(0, degree), CoefficientHigh(0, degree), 1);
            resultLow = nextLow;
        }

        return Multiply(resultLow, resultHigh, low, high, word);
    }
}
