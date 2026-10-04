namespace WarpCLR.Compiler;

internal static partial class WarpPortableBinary64Transcendentals
{
    private static uint ArcTangent(uint low, uint high, uint word)
    {
        uint sign = high >> 31;
        high &= 0x7FFFFFFFu;
        if (IsNaN(low, high) != 0) { return word == 0 ? 0 : 0x7FF80000u; }
        if (high == 0x7FF00000u) { return word == 0 ? ConstantLow(2) : ConstantHigh(2) | sign << 31; }
        if (high < 0x3E400000u) { return word == 0 ? low : high | sign << 31; }
        uint reciprocal = LessMagnitude(0, 0x3FF00000u, low, high);
        if (reciprocal != 0)
        {
            uint nextLow = Divide(0, 0x3FF00000u, low, high, 0);
            high = Divide(0, 0x3FF00000u, low, high, 1);
            low = nextLow;
        }

        uint offset = LessMagnitude(ConstantLow(9), ConstantHigh(9), low, high);
        if (offset != 0)
        {
            uint nextLow = LogRatio(low, high, 0);
            high = LogRatio(low, high, 1);
            low = nextLow;
        }

        uint squareLow = Multiply(low, high, low, high, 0);
        uint squareHigh = Multiply(low, high, low, high, 1);
        uint resultLow = Multiply(low, high, Polynomial(squareLow, squareHigh, 3, 40, 0), Polynomial(squareLow, squareHigh, 3, 40, 1), 0);
        uint resultHigh = Multiply(low, high, Polynomial(squareLow, squareHigh, 3, 40, 0), Polynomial(squareLow, squareHigh, 3, 40, 1), 1);
        if (offset != 0)
        {
            uint nextLow = Add(resultLow, resultHigh, ConstantLow(3), ConstantHigh(3), 0);
            resultHigh = Add(resultLow, resultHigh, ConstantLow(3), ConstantHigh(3), 1);
            resultLow = nextLow;
        }

        if (reciprocal != 0)
        {
            uint nextLow = Subtract(ConstantLow(2), ConstantHigh(2), resultLow, resultHigh, 0);
            resultHigh = Subtract(ConstantLow(2), ConstantHigh(2), resultLow, resultHigh, 1);
            resultLow = nextLow;
        }

        return word == 0 ? resultLow : resultHigh ^ sign << 31;
    }

    private static uint ArcTangent2(uint yLow, uint yHigh, uint xLow, uint xHigh, uint word)
    {
        uint sign = yHigh >> 31;
        uint xSign = xHigh >> 31;
        yHigh &= 0x7FFFFFFFu;
        xHigh &= 0x7FFFFFFFu;
        if (IsNaN(yLow, yHigh) != 0 || IsNaN(xLow, xHigh) != 0) { return word == 0 ? 0 : 0x7FF80000u; }
        if (IsZero(yLow, yHigh) != 0) { return xSign == 0 ? word == 0 ? 0 : sign << 31 : word == 0 ? ConstantLow(1) : ConstantHigh(1) | sign << 31; }
        if (IsZero(xLow, xHigh) != 0 || (yHigh == 0x7FF00000u && xHigh != 0x7FF00000u))
        {
            return word == 0 ? ConstantLow(2) : ConstantHigh(2) | sign << 31;
        }

        if (xHigh == 0x7FF00000u && yHigh != 0x7FF00000u)
        {
            return xSign == 0 ? word == 0 ? 0 : sign << 31 : word == 0 ? ConstantLow(1) : ConstantHigh(1) | sign << 31;
        }

        uint resultLow = yHigh == 0x7FF00000u ? ConstantLow(3) :
            ArcTangent(Divide(yLow, yHigh, xLow, xHigh, 0), Divide(yLow, yHigh, xLow, xHigh, 1), 0);
        uint resultHigh = yHigh == 0x7FF00000u ? ConstantHigh(3) :
            ArcTangent(Divide(yLow, yHigh, xLow, xHigh, 0), Divide(yLow, yHigh, xLow, xHigh, 1), 1);
        if (xSign != 0)
        {
            uint nextLow = Subtract(ConstantLow(1), ConstantHigh(1), resultLow, resultHigh, 0);
            resultHigh = Subtract(ConstantLow(1), ConstantHigh(1), resultLow, resultHigh, 1);
            resultLow = nextLow;
        }

        return word == 0 ? resultLow : resultHigh | sign << 31;
    }

    private static uint ArcSineCosine(uint low, uint high, uint cosine, uint word)
    {
        uint magnitude = high & 0x7FFFFFFFu;
        if (IsNaN(low, high) != 0 || LessMagnitude(0, 0x3FF00000u, low, magnitude) != 0) { return word == 0 ? 0 : 0x7FF80000u; }
        uint productLow = Multiply(Subtract(0, 0x3FF00000u, low, high, 0), Subtract(0, 0x3FF00000u, low, high, 1),
            Add(0, 0x3FF00000u, low, high, 0), Add(0, 0x3FF00000u, low, high, 1), 0);
        uint productHigh = Multiply(Subtract(0, 0x3FF00000u, low, high, 0), Subtract(0, 0x3FF00000u, low, high, 1),
            Add(0, 0x3FF00000u, low, high, 0), Add(0, 0x3FF00000u, low, high, 1), 1);
        uint rootLow = Root(productLow, productHigh, 0);
        uint rootHigh = Root(productLow, productHigh, 1);
        return cosine == 0 ? ArcTangent2(low, high, rootLow, rootHigh, word) : ArcTangent2(rootLow, rootHigh, low, high, word);
    }
}
