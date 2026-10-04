namespace WarpCLR.Compiler;

internal static partial class WarpPortableBinary64Transcendentals
{
    private static uint Logarithm(uint low, uint high, uint kind, uint word)
    {
        if (IsNaN(low, high) != 0 || ((high >> 31) != 0 && IsZero(low, high) == 0)) { return word == 0 ? 0 : 0x7FF80000u; }
        if (IsZero(low, high) != 0) { return word == 0 ? 0 : 0xFFF00000u; }
        if (high == 0x7FF00000u) { return word == 0 ? 0 : high; }
        uint exponent = WarpPortableBinary64Intrinsics.ILogB(low, high);
        uint normalizedLow = Scale(low, high, unchecked(0u - exponent), 0);
        uint normalizedHigh = Scale(low, high, unchecked(0u - exponent), 1);
        if (kind == 1 && normalizedLow == 0 && normalizedHigh == 0x3FF00000u) { return SignedInteger(exponent, word); }
        if (LessMagnitude(ConstantLow(8), ConstantHigh(8), normalizedLow, normalizedHigh) != 0)
        {
            normalizedHigh -= 0x100000u;
            exponent = unchecked(exponent + 1);
        }

        uint ratioLow = LogRatio(normalizedLow, normalizedHigh, 0);
        uint ratioHigh = LogRatio(normalizedLow, normalizedHigh, 1);
        uint squareLow = Multiply(ratioLow, ratioHigh, ratioLow, ratioHigh, 0);
        uint squareHigh = Multiply(ratioLow, ratioHigh, ratioLow, ratioHigh, 1);
        uint polynomialLow = Polynomial(squareLow, squareHigh, 4, 20, 0);
        uint polynomialHigh = Polynomial(squareLow, squareHigh, 4, 20, 1);
        ratioHigh = IsZero(ratioLow, ratioHigh) != 0 ? ratioHigh : ratioHigh + 0x100000u;
        uint fractionLow = Multiply(ratioLow, ratioHigh, polynomialLow, polynomialHigh, 0);
        uint fractionHigh = Multiply(ratioLow, ratioHigh, polynomialLow, polynomialHigh, 1);
        uint integerLow = SignedInteger(exponent, 0);
        uint integerHigh = SignedInteger(exponent, 1);
        uint remainderLow = Fused(integerLow, integerHigh, ConstantLow(5), ConstantHigh(5), fractionLow, fractionHigh, 0);
        uint remainderHigh = Fused(integerLow, integerHigh, ConstantLow(5), ConstantHigh(5), fractionLow, fractionHigh, 1);
        uint resultLow = Fused(integerLow, integerHigh, ConstantLow(4), ConstantHigh(4), remainderLow, remainderHigh, 0);
        uint resultHigh = Fused(integerLow, integerHigh, ConstantLow(4), ConstantHigh(4), remainderLow, remainderHigh, 1);
        return kind == 0 ? word == 0 ? resultLow : resultHigh :
            Multiply(resultLow, resultHigh, ConstantLow(kind == 1 ? 6u : 7u), ConstantHigh(kind == 1 ? 6u : 7u), word);
    }

    private static uint LogRatio(uint low, uint high, uint word) =>
        Divide(Subtract(low, high, 0, 0x3FF00000u, 0), Subtract(low, high, 0, 0x3FF00000u, 1),
            Add(low, high, 0, 0x3FF00000u, 0), Add(low, high, 0, 0x3FF00000u, 1), word);

    private static uint LogarithmOnePlus(uint low, uint high, uint word)
    {
        if ((high & 0x7FFFFFFFu) > 0x3FC00000u)
        {
            return Logarithm(Add(low, high, 0, 0x3FF00000u, 0), Add(low, high, 0, 0x3FF00000u, 1), 0, word);
        }

        return Multiply(low, high, Polynomial(low, high, 5, 40, 0), Polynomial(low, high, 5, 40, 1), word);
    }
}
