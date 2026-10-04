namespace WarpCLR.Compiler;

internal static partial class WarpPortableBinary64Transcendentals
{
    private static uint Hyperbolic(uint low, uint high, uint kind, uint word)
    {
        uint sign = high >> 31;
        high &= 0x7FFFFFFFu;
        if (IsNaN(low, high) != 0) { return word == 0 ? 0 : 0x7FF80000u; }
        if (high < 0x3E300000u) { return kind == 1 ? Constant(0, word) : word == 0 ? low : high | sign << 31; }
        if (kind == 2 && high >= 0x40340000u) { return word == 0 ? 0 : 0x3FF00000u | sign << 31; }
        uint resultLow;
        uint resultHigh;
        if (kind == 2)
        {
            uint exponentialLow = ExponentialMinusOne(low, high + 0x100000u, 0);
            uint exponentialHigh = ExponentialMinusOne(low, high + 0x100000u, 1);
            uint denominatorLow = Add(exponentialLow, exponentialHigh, 0, 0x40000000u, 0);
            uint denominatorHigh = Add(exponentialLow, exponentialHigh, 0, 0x40000000u, 1);
            resultLow = Divide(exponentialLow, exponentialHigh, denominatorLow, denominatorHigh, 0);
            resultHigh = Divide(exponentialLow, exponentialHigh, denominatorLow, denominatorHigh, 1);
        }
        else
        {
            resultLow = high < 0x3FF00000u ? HyperbolicSmall(low, high, kind, 0) : HyperbolicLarge(low, high, kind, 0);
            resultHigh = high < 0x3FF00000u ? HyperbolicSmall(low, high, kind, 1) : HyperbolicLarge(low, high, kind, 1);
        }

        return word == 0 ? resultLow : resultHigh | (kind == 1 ? 0 : sign << 31);
    }

    private static uint HyperbolicSmall(uint low, uint high, uint kind, uint word)
    {
        uint exponentialLow = ExponentialMinusOne(low, high, 0);
        uint exponentialHigh = ExponentialMinusOne(low, high, 1);
        uint denominatorLow = Add(exponentialLow, exponentialHigh, 0, 0x3FF00000u, 0);
        uint denominatorHigh = Add(exponentialLow, exponentialHigh, 0, 0x3FF00000u, 1);
        uint fractionLow = Divide(exponentialLow, exponentialHigh, denominatorLow, denominatorHigh, 0);
        uint fractionHigh = Divide(exponentialLow, exponentialHigh, denominatorLow, denominatorHigh, 1);
        if (kind == 0)
        {
            return Scale(Add(exponentialLow, exponentialHigh, fractionLow, fractionHigh, 0),
                Add(exponentialLow, exponentialHigh, fractionLow, fractionHigh, 1), 0xFFFFFFFFu, word);
        }

        return Fused(exponentialLow, exponentialHigh,
            Scale(fractionLow, fractionHigh, 0xFFFFFFFFu, 0), Scale(fractionLow, fractionHigh, 0xFFFFFFFFu, 1), 0, 0x3FF00000u, word);
    }

    private static uint HyperbolicLarge(uint low, uint high, uint kind, uint word)
    {
        if (high >= 0x40890000u) { return word == 0 ? 0 : 0x7FF00000u; }
        uint exponentialLow = Exponential(low, high - 0x100000u, 0);
        uint exponentialHigh = Exponential(low, high - 0x100000u, 1);
        uint fractionLow = Divide(0, 0x3FF00000u, exponentialLow, exponentialHigh, 0);
        uint fractionHigh = Divide(0, 0x3FF00000u, exponentialLow, exponentialHigh, 1);
        uint primaryLow = Multiply(Scale(exponentialLow, exponentialHigh, 0xFFFFFFFFu, 0),
            Scale(exponentialLow, exponentialHigh, 0xFFFFFFFFu, 1), exponentialLow, exponentialHigh, 0);
        uint primaryHigh = Multiply(Scale(exponentialLow, exponentialHigh, 0xFFFFFFFFu, 0),
            Scale(exponentialLow, exponentialHigh, 0xFFFFFFFFu, 1), exponentialLow, exponentialHigh, 1);
        uint correctionLow = Multiply(Scale(fractionLow, fractionHigh, 0xFFFFFFFFu, 0),
            Scale(fractionLow, fractionHigh, 0xFFFFFFFFu, 1), fractionLow, fractionHigh, 0);
        uint correctionHigh = Multiply(Scale(fractionLow, fractionHigh, 0xFFFFFFFFu, 0),
            Scale(fractionLow, fractionHigh, 0xFFFFFFFFu, 1), fractionLow, fractionHigh, 1);
        return Add(primaryLow, primaryHigh, correctionLow, correctionHigh ^ (kind == 0 ? 0x80000000u : 0), word);
    }

    private static uint InverseHyperbolic(uint low, uint high, uint kind, uint word)
    {
        uint sign = high >> 31;
        uint magnitude = high & 0x7FFFFFFFu;
        if (IsNaN(low, high) != 0) { return word == 0 ? 0 : 0x7FF80000u; }
        if (kind == 1 && (sign != 0 || LessMagnitude(low, magnitude, 0, 0x3FF00000u) != 0)) { return word == 0 ? 0 : 0x7FF80000u; }
        if (kind == 2 && LessMagnitude(0, 0x3FF00000u, low, magnitude) != 0) { return word == 0 ? 0 : 0x7FF80000u; }
        if (kind == 2 && low == 0 && magnitude == 0x3FF00000u) { return word == 0 ? 0 : 0x7FF00000u | sign << 31; }
        if (kind != 1 && magnitude < 0x3E300000u) { return word == 0 ? low : high; }
        if (kind != 2 && magnitude >= 0x41B00000u)
        {
            uint result = Add(Logarithm(low, magnitude, 0, 0), Logarithm(low, magnitude, 0, 1), ConstantLow(11), ConstantHigh(11), word);
            return word == 0 ? result : result | (kind == 0 ? sign << 31 : 0);
        }

        uint argumentLow = InverseHyperbolicArgument(low, magnitude, kind, 0);
        uint argumentHigh = InverseHyperbolicArgument(low, magnitude, kind, 1);
        uint resultLow = LogarithmOnePlus(argumentLow, argumentHigh, 0);
        uint resultHigh = LogarithmOnePlus(argumentLow, argumentHigh, 1);
        if (kind == 2)
        {
            resultLow = Scale(resultLow, resultHigh, 0xFFFFFFFFu, 0);
            resultHigh = Scale(LogarithmOnePlus(argumentLow, argumentHigh, 0), resultHigh, 0xFFFFFFFFu, 1);
        }

        return word == 0 ? resultLow : resultHigh | (kind == 1 ? 0 : sign << 31);
    }

    private static uint InverseHyperbolicArgument(uint low, uint high, uint kind, uint word)
    {
        if (kind == 2)
        {
            return Divide(low, high + 0x100000u, Subtract(0, 0x3FF00000u, low, high, 0),
                Subtract(0, 0x3FF00000u, low, high, 1), word);
        }

        if (kind == 1)
        {
            uint differenceLow = Subtract(low, high, 0, 0x3FF00000u, 0);
            uint differenceHigh = Subtract(low, high, 0, 0x3FF00000u, 1);
            uint sumLow = Add(low, high, 0, 0x3FF00000u, 0);
            uint sumHigh = Add(low, high, 0, 0x3FF00000u, 1);
            return Add(differenceLow, differenceHigh,
                Multiply(Root(differenceLow, differenceHigh, 0), Root(differenceLow, differenceHigh, 1), Root(sumLow, sumHigh, 0), Root(sumLow, sumHigh, 1), 0),
                Multiply(Root(differenceLow, differenceHigh, 0), Root(differenceLow, differenceHigh, 1), Root(sumLow, sumHigh, 0), Root(sumLow, sumHigh, 1), 1), word);
        }

        uint squareLow = Multiply(low, high, low, high, 0);
        uint squareHigh = Multiply(low, high, low, high, 1);
        uint rootLow = Root(Add(squareLow, squareHigh, 0, 0x3FF00000u, 0), Add(squareLow, squareHigh, 0, 0x3FF00000u, 1), 0);
        uint rootHigh = Root(Add(squareLow, squareHigh, 0, 0x3FF00000u, 0), Add(squareLow, squareHigh, 0, 0x3FF00000u, 1), 1);
        return Add(low, high, Divide(squareLow, squareHigh, Add(rootLow, rootHigh, 0, 0x3FF00000u, 0), Add(rootLow, rootHigh, 0, 0x3FF00000u, 1), 0),
            Divide(squareLow, squareHigh, Add(rootLow, rootHigh, 0, 0x3FF00000u, 0), Add(rootLow, rootHigh, 0, 0x3FF00000u, 1), 1), word);
    }
}
