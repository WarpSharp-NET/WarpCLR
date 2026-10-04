namespace WarpCLR.Compiler;

internal static partial class WarpPortableBinary64Transcendentals
{
    private static uint PreciseLogarithm(uint low, uint high, uint word)
    {
        uint exponent = WarpPortableBinary64Intrinsics.ILogB(low, high);
        uint normalizedLow = Scale(low, high, unchecked(0u - exponent), 0);
        uint normalizedHigh = Scale(low, high, unchecked(0u - exponent), 1);
        if (LessMagnitude(ConstantLow(8), ConstantHigh(8), normalizedLow, normalizedHigh) != 0)
        {
            normalizedHigh -= 0x100000u;
            exponent = unchecked(exponent + 1);
        }

        uint fraction0 = PreciseLogarithmFraction(normalizedLow, normalizedHigh, 0);
        uint fraction1 = PreciseLogarithmFraction(normalizedLow, normalizedHigh, 1);
        uint fraction2 = PreciseLogarithmFraction(normalizedLow, normalizedHigh, 2);
        uint fraction3 = PreciseLogarithmFraction(normalizedLow, normalizedHigh, 3);
        uint integerLow = SignedInteger(exponent, 0);
        uint integerHigh = SignedInteger(exponent, 1);
        uint product0 = DoubleDoubleMultiply(integerLow, integerHigh, 0, 0, ConstantLow(4), ConstantHigh(4), ConstantLow(5), ConstantHigh(5), 0);
        uint product1 = DoubleDoubleMultiply(integerLow, integerHigh, 0, 0, ConstantLow(4), ConstantHigh(4), ConstantLow(5), ConstantHigh(5), 1);
        uint product2 = DoubleDoubleMultiply(integerLow, integerHigh, 0, 0, ConstantLow(4), ConstantHigh(4), ConstantLow(5), ConstantHigh(5), 2);
        uint product3 = DoubleDoubleMultiply(integerLow, integerHigh, 0, 0, ConstantLow(4), ConstantHigh(4), ConstantLow(5), ConstantHigh(5), 3);
        return DoubleDoubleAdd(product0, product1, product2, product3, fraction0, fraction1, fraction2, fraction3, word);
    }

    private static uint PreciseLogarithmFraction(uint low, uint high, uint word)
    {
        uint ratio0 = DoubleDoubleRatio(low, high, 0);
        uint ratio1 = DoubleDoubleRatio(low, high, 1);
        uint ratio2 = DoubleDoubleRatio(low, high, 2);
        uint ratio3 = DoubleDoubleRatio(low, high, 3);
        uint square0 = DoubleDoubleMultiply(ratio0, ratio1, ratio2, ratio3, ratio0, ratio1, ratio2, ratio3, 0);
        uint square1 = DoubleDoubleMultiply(ratio0, ratio1, ratio2, ratio3, ratio0, ratio1, ratio2, ratio3, 1);
        uint square2 = DoubleDoubleMultiply(ratio0, ratio1, ratio2, ratio3, ratio0, ratio1, ratio2, ratio3, 2);
        uint square3 = DoubleDoubleMultiply(ratio0, ratio1, ratio2, ratio3, ratio0, ratio1, ratio2, ratio3, 3);
        uint polynomial0 = PreciseLogarithmPolynomial(square0, square1, square2, square3, 0);
        uint polynomial1 = PreciseLogarithmPolynomial(square0, square1, square2, square3, 1);
        uint polynomial2 = PreciseLogarithmPolynomial(square0, square1, square2, square3, 2);
        uint polynomial3 = PreciseLogarithmPolynomial(square0, square1, square2, square3, 3);
        uint primary0 = DoubleDoubleMultiply(ratio0, ratio1, ratio2, ratio3, polynomial0, polynomial1, polynomial2, polynomial3, 0);
        uint primary1 = DoubleDoubleMultiply(ratio0, ratio1, ratio2, ratio3, polynomial0, polynomial1, polynomial2, polynomial3, 1);
        uint tail0 = DoubleDoubleMultiply(ratio0, ratio1, ratio2, ratio3, polynomial0, polynomial1, polynomial2, polynomial3, 2);
        uint tail1 = DoubleDoubleMultiply(ratio0, ratio1, ratio2, ratio3, polynomial0, polynomial1, polynomial2, polynomial3, 3);
        return word < 2 ? Scale(primary0, primary1, 1, word) : Scale(tail0, tail1, 1, word - 2);
    }

    private static uint PreciseLogarithmPolynomial(uint low, uint high, uint tailLow, uint tailHigh, uint word)
    {
        uint result0 = CoefficientLow(4, 20);
        uint result1 = CoefficientHigh(4, 20);
        uint result2 = LogTailLow(20);
        uint result3 = LogTailHigh(20);
        for (uint degree = 20; degree != 0; degree--)
        {
            uint product0 = DoubleDoubleMultiply(result0, result1, result2, result3, low, high, tailLow, tailHigh, 0);
            uint product1 = DoubleDoubleMultiply(result0, result1, result2, result3, low, high, tailLow, tailHigh, 1);
            uint product2 = DoubleDoubleMultiply(result0, result1, result2, result3, low, high, tailLow, tailHigh, 2);
            uint product3 = DoubleDoubleMultiply(result0, result1, result2, result3, low, high, tailLow, tailHigh, 3);
            result0 = DoubleDoubleAdd(product0, product1, product2, product3, CoefficientLow(4, degree - 1), CoefficientHigh(4, degree - 1), LogTailLow(degree - 1), LogTailHigh(degree - 1), 0);
            result1 = DoubleDoubleAdd(product0, product1, product2, product3, CoefficientLow(4, degree - 1), CoefficientHigh(4, degree - 1), LogTailLow(degree - 1), LogTailHigh(degree - 1), 1);
            result2 = DoubleDoubleAdd(product0, product1, product2, product3, CoefficientLow(4, degree - 1), CoefficientHigh(4, degree - 1), LogTailLow(degree - 1), LogTailHigh(degree - 1), 2);
            result3 = DoubleDoubleAdd(product0, product1, product2, product3, CoefficientLow(4, degree - 1), CoefficientHigh(4, degree - 1), LogTailLow(degree - 1), LogTailHigh(degree - 1), 3);
        }

        return word < 2 ? word == 0 ? result0 : result1 : word == 2 ? result2 : result3;
    }

    private static uint PrecisePowerFinite(uint low, uint high, uint exponentLow, uint exponentHigh, uint word)
    {
        uint approximate0 = Multiply(Logarithm(low, high, 0, 0), Logarithm(low, high, 0, 1), exponentLow, exponentHigh, 0);
        uint approximate1 = Multiply(Logarithm(low, high, 0, 0), Logarithm(low, high, 0, 1), exponentLow, exponentHigh, 1);
        if ((approximate1 & 0x7FFFFFFFu) >= 0x40890000u) { return Exponential(approximate0, approximate1, word); }
        uint logarithm0 = PreciseLogarithm(low, high, 0);
        uint logarithm1 = PreciseLogarithm(low, high, 1);
        uint logarithm2 = PreciseLogarithm(low, high, 2);
        uint logarithm3 = PreciseLogarithm(low, high, 3);
        uint product0 = DoubleDoubleMultiply(logarithm0, logarithm1, logarithm2, logarithm3, exponentLow, exponentHigh, 0, 0, 0);
        uint product1 = DoubleDoubleMultiply(logarithm0, logarithm1, logarithm2, logarithm3, exponentLow, exponentHigh, 0, 0, 1);
        uint product2 = DoubleDoubleMultiply(logarithm0, logarithm1, logarithm2, logarithm3, exponentLow, exponentHigh, 0, 0, 2);
        uint product3 = DoubleDoubleMultiply(logarithm0, logarithm1, logarithm2, logarithm3, exponentLow, exponentHigh, 0, 0, 3);
        return ExponentialWithTail(product0, product1, product2, product3, word);
    }
}
