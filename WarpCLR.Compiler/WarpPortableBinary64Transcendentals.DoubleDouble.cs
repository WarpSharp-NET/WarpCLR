namespace WarpCLR.Compiler;

internal static partial class WarpPortableBinary64Transcendentals
{
    private static uint DoubleDoubleNormalize(uint low, uint high, uint tailLow, uint tailHigh, uint word)
    {
        uint sumLow = Add(low, high, tailLow, tailHigh, 0);
        uint sumHigh = Add(low, high, tailLow, tailHigh, 1);
        if (word < 2) { return word == 0 ? sumLow : sumHigh; }
        return Add(Subtract(low, high, sumLow, sumHigh, 0), Subtract(low, high, sumLow, sumHigh, 1), tailLow, tailHigh, word - 2);
    }

    private static uint DoubleDoubleAdd(uint leftLow, uint leftHigh, uint leftTailLow, uint leftTailHigh,
        uint rightLow, uint rightHigh, uint rightTailLow, uint rightTailHigh, uint word)
    {
        uint sumLow = Add(leftLow, leftHigh, rightLow, rightHigh, 0);
        uint sumHigh = Add(leftLow, leftHigh, rightLow, rightHigh, 1);
        uint recoveredLow = Subtract(sumLow, sumHigh, leftLow, leftHigh, 0);
        uint recoveredHigh = Subtract(sumLow, sumHigh, leftLow, leftHigh, 1);
        uint correctionLow = Add(Subtract(leftLow, leftHigh, Subtract(sumLow, sumHigh, recoveredLow, recoveredHigh, 0),
            Subtract(sumLow, sumHigh, recoveredLow, recoveredHigh, 1), 0),
            Subtract(leftLow, leftHigh, Subtract(sumLow, sumHigh, recoveredLow, recoveredHigh, 0),
            Subtract(sumLow, sumHigh, recoveredLow, recoveredHigh, 1), 1),
            Subtract(rightLow, rightHigh, recoveredLow, recoveredHigh, 0), Subtract(rightLow, rightHigh, recoveredLow, recoveredHigh, 1), 0);
        uint correctionHigh = Add(Subtract(leftLow, leftHigh, Subtract(sumLow, sumHigh, recoveredLow, recoveredHigh, 0),
            Subtract(sumLow, sumHigh, recoveredLow, recoveredHigh, 1), 0),
            Subtract(leftLow, leftHigh, Subtract(sumLow, sumHigh, recoveredLow, recoveredHigh, 0),
            Subtract(sumLow, sumHigh, recoveredLow, recoveredHigh, 1), 1),
            Subtract(rightLow, rightHigh, recoveredLow, recoveredHigh, 0), Subtract(rightLow, rightHigh, recoveredLow, recoveredHigh, 1), 1);
        uint tailsLow = Add(leftTailLow, leftTailHigh, rightTailLow, rightTailHigh, 0);
        uint tailsHigh = Add(leftTailLow, leftTailHigh, rightTailLow, rightTailHigh, 1);
        return DoubleDoubleNormalize(sumLow, sumHigh, Add(correctionLow, correctionHigh, tailsLow, tailsHigh, 0),
            Add(correctionLow, correctionHigh, tailsLow, tailsHigh, 1), word);
    }

    private static uint DoubleDoubleMultiply(uint leftLow, uint leftHigh, uint leftTailLow, uint leftTailHigh,
        uint rightLow, uint rightHigh, uint rightTailLow, uint rightTailHigh, uint word)
    {
        uint productLow = Multiply(leftLow, leftHigh, rightLow, rightHigh, 0);
        uint productHigh = Multiply(leftLow, leftHigh, rightLow, rightHigh, 1);
        uint errorLow = Fused(leftLow, leftHigh, rightLow, rightHigh, productLow, productHigh ^ 0x80000000u, 0);
        uint errorHigh = Fused(leftLow, leftHigh, rightLow, rightHigh, productLow, productHigh ^ 0x80000000u, 1);
        uint correctionLow = Fused(leftLow, leftHigh, rightTailLow, rightTailHigh, errorLow, errorHigh, 0);
        uint correctionHigh = Fused(leftLow, leftHigh, rightTailLow, rightTailHigh, errorLow, errorHigh, 1);
        errorLow = Fused(leftTailLow, leftTailHigh, rightLow, rightHigh, correctionLow, correctionHigh, 0);
        errorHigh = Fused(leftTailLow, leftTailHigh, rightLow, rightHigh, correctionLow, correctionHigh, 1);
        return DoubleDoubleNormalize(productLow, productHigh, errorLow, errorHigh, word);
    }

    private static uint DoubleDoubleRatio(uint low, uint high, uint word)
    {
        uint numeratorLow = Subtract(low, high, 0, 0x3FF00000u, 0);
        uint numeratorHigh = Subtract(low, high, 0, 0x3FF00000u, 1);
        uint denominatorLow = Add(low, high, 0, 0x3FF00000u, 0);
        uint denominatorHigh = Add(low, high, 0, 0x3FF00000u, 1);
        uint denominatorTailLow = Subtract(low, high, Subtract(denominatorLow, denominatorHigh, 0, 0x3FF00000u, 0),
            Subtract(denominatorLow, denominatorHigh, 0, 0x3FF00000u, 1), 0);
        uint denominatorTailHigh = Subtract(low, high, Subtract(denominatorLow, denominatorHigh, 0, 0x3FF00000u, 0),
            Subtract(denominatorLow, denominatorHigh, 0, 0x3FF00000u, 1), 1);
        uint quotientLow = Divide(numeratorLow, numeratorHigh, denominatorLow, denominatorHigh, 0);
        uint quotientHigh = Divide(numeratorLow, numeratorHigh, denominatorLow, denominatorHigh, 1);
        uint residualLow = Fused(quotientLow, quotientHigh ^ 0x80000000u, denominatorLow, denominatorHigh, numeratorLow, numeratorHigh, 0);
        uint residualHigh = Fused(quotientLow, quotientHigh ^ 0x80000000u, denominatorLow, denominatorHigh, numeratorLow, numeratorHigh, 1);
        uint nextLow = Fused(quotientLow, quotientHigh ^ 0x80000000u, denominatorTailLow, denominatorTailHigh, residualLow, residualHigh, 0);
        residualHigh = Fused(quotientLow, quotientHigh ^ 0x80000000u, denominatorTailLow, denominatorTailHigh, residualLow, residualHigh, 1);
        residualLow = nextLow;
        return DoubleDoubleNormalize(quotientLow, quotientHigh, Divide(residualLow, residualHigh, denominatorLow, denominatorHigh, 0),
            Divide(residualLow, residualHigh, denominatorLow, denominatorHigh, 1), word);
    }
}
