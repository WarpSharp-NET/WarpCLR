namespace WarpCLR.Compiler;

internal static partial class WarpPortableBinary64Transcendentals
{
    private static uint IsNaN(uint low, uint high) =>
        (high & 0x7FF00000u) == 0x7FF00000u && ((high & 0xFFFFFu) | low) != 0 ? 1u : 0u;

    private static uint IsZero(uint low, uint high) => ((high & 0x7FFFFFFFu) | low) == 0 ? 1u : 0u;

    private static uint LessMagnitude(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        leftHigh < rightHigh || (leftHigh == rightHigh && leftLow < rightLow) ? 1u : 0u;

    private static uint Constant(uint kind, uint word) => word == 0 ? ConstantLow(kind) : ConstantHigh(kind);

    private static uint Add(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh, uint word) =>
        word == 0 ? WarpPortableBinary64.AddLow(leftLow, leftHigh, rightLow, rightHigh) :
            WarpPortableBinary64.AddHigh(leftLow, leftHigh, rightLow, rightHigh);

    private static uint Subtract(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh, uint word) =>
        Add(leftLow, leftHigh, rightLow, rightHigh ^ 0x80000000u, word);

    private static uint Multiply(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh, uint word) =>
        word == 0 ? WarpPortableBinary64.MultiplyLow(leftLow, leftHigh, rightLow, rightHigh) :
            WarpPortableBinary64.MultiplyHigh(leftLow, leftHigh, rightLow, rightHigh);

    private static uint Divide(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh, uint word) =>
        word == 0 ? WarpPortableBinary64.DivideLow(leftLow, leftHigh, rightLow, rightHigh) :
            WarpPortableBinary64.DivideHigh(leftLow, leftHigh, rightLow, rightHigh);

    private static uint Fused(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh,
        uint addendLow, uint addendHigh, uint word) =>
        word == 0 ? WarpPortableBinary64Math.FusedMultiplyAddLow(leftLow, leftHigh, rightLow, rightHigh, addendLow, addendHigh) :
            WarpPortableBinary64Math.FusedMultiplyAddHigh(leftLow, leftHigh, rightLow, rightHigh, addendLow, addendHigh);

    private static uint Scale(uint low, uint high, uint exponent, uint word) =>
        word == 0 ? WarpPortableBinary64Intrinsics.ScaleBLow(low, high, exponent) :
            WarpPortableBinary64Intrinsics.ScaleBHigh(low, high, exponent);

    private static uint Root(uint low, uint high, uint word) =>
        word == 0 ? WarpPortableBinary64Math.SqrtLow(low, high) : WarpPortableBinary64Math.SqrtHigh(low, high);

    private static uint SignedInteger(uint value, uint word) =>
        word == 0 ? WarpPortableNumericConversions.Int32ToDoubleLow(value) : WarpPortableNumericConversions.Int32ToDoubleHigh(value);

    private static uint Polynomial(uint low, uint high, uint kind, uint degree, uint word)
    {
        uint resultLow = CoefficientLow(kind, degree);
        uint resultHigh = CoefficientHigh(kind, degree);
        while (degree != 0)
        {
            degree--;
            uint nextLow = Fused(resultLow, resultHigh, low, high, CoefficientLow(kind, degree), CoefficientHigh(kind, degree), 0);
            resultHigh = Fused(resultLow, resultHigh, low, high, CoefficientLow(kind, degree), CoefficientHigh(kind, degree), 1);
            resultLow = nextLow;
        }

        return word == 0 ? resultLow : resultHigh;
    }

    private static uint MultiplyWordHigh(uint left, uint right)
    {
        uint leftLow = left & 0xFFFFu;
        uint rightLow = right & 0xFFFFu;
        uint lowProduct = leftLow * rightLow;
        uint middle = (left >> 16) * rightLow + (lowProduct >> 16);
        uint middleLow = middle & 0xFFFFu;
        uint middleHigh = middle >> 16;
        middleLow += leftLow * (right >> 16);
        return (left >> 16) * (right >> 16) + middleHigh + (middleLow >> 16);
    }
}
