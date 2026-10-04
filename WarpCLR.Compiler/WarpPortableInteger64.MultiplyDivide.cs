namespace WarpCLR.Compiler;

internal static partial class WarpPortableInteger64
{
    public static uint DivideUnsignedLow(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        DivideRemainder(leftLow, leftHigh, rightLow, rightHigh, 0);

    public static uint DivideUnsignedHigh(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        DivideRemainder(leftLow, leftHigh, rightLow, rightHigh, 1);

    public static uint RemainderUnsignedLow(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        DivideRemainder(leftLow, leftHigh, rightLow, rightHigh, 2);

    public static uint RemainderUnsignedHigh(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        DivideRemainder(leftLow, leftHigh, rightLow, rightHigh, 3);

    public static uint DivideSignedLow(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        DivideRemainderSigned(leftLow, leftHigh, rightLow, rightHigh, 0);

    public static uint DivideSignedHigh(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        DivideRemainderSigned(leftLow, leftHigh, rightLow, rightHigh, 1);

    public static uint RemainderSignedLow(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        DivideRemainderSigned(leftLow, leftHigh, rightLow, rightHigh, 2);

    public static uint RemainderSignedHigh(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        DivideRemainderSigned(leftLow, leftHigh, rightLow, rightHigh, 3);

    public static uint DivideUnsignedFault(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        (rightLow | rightHigh) == 0 ? WarpPortableIntegerFault.DivideByZero : WarpPortableIntegerFault.None;

    public static uint DivideSignedFault(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        (rightLow | rightHigh) == 0 ? WarpPortableIntegerFault.DivideByZero :
        leftLow == 0 && leftHigh == 0x80000000u && rightLow == 0xFFFFFFFFu && rightHigh == 0xFFFFFFFFu ?
            WarpPortableIntegerFault.Overflow : WarpPortableIntegerFault.None;

    public static uint MultiplyUnsignedFault(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        (MultiplyUpper(leftLow, leftHigh, rightLow, rightHigh, 0) | MultiplyUpper(leftLow, leftHigh, rightLow, rightHigh, 1)) != 0 ?
            WarpPortableIntegerFault.Overflow : WarpPortableIntegerFault.None;

    public static uint MultiplySignedFault(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh)
    {
        uint sign = (leftHigh ^ rightHigh) >> 31;
        if ((leftHigh >> 31) != 0)
        {
            leftHigh = NegateHigh(leftLow, leftHigh);
            leftLow = NegateLow(leftLow, leftHigh);
        }

        if ((rightHigh >> 31) != 0)
        {
            rightHigh = NegateHigh(rightLow, rightHigh);
            rightLow = NegateLow(rightLow, rightHigh);
        }

        if (MultiplyUnsignedFault(leftLow, leftHigh, rightLow, rightHigh) != WarpPortableIntegerFault.None)
        {
            return WarpPortableIntegerFault.Overflow;
        }

        uint low = MultiplyLow(leftLow, leftHigh, rightLow, rightHigh);
        uint high = MultiplyHigh(leftLow, leftHigh, rightLow, rightHigh);
        bool overflow = sign == 0 ? (high >> 31) != 0 : high > 0x80000000u || (high == 0x80000000u && low != 0);
        return overflow ? WarpPortableIntegerFault.Overflow : WarpPortableIntegerFault.None;
    }

    private static uint MultiplyUpper(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh, uint word)
    {
        uint middle = WarpPortableWordMath.MultiplyHigh(leftLow, rightLow);
        uint term = unchecked(leftLow * rightHigh);
        uint sum = unchecked(middle + term);
        uint carry = sum < middle ? 1u : 0u;
        middle = sum;
        term = unchecked(leftHigh * rightLow);
        sum = unchecked(middle + term);
        carry += sum < middle ? 1u : 0u;

        uint upper = unchecked(leftHigh * rightHigh);
        uint top = WarpPortableWordMath.MultiplyHigh(leftHigh, rightHigh);
        term = WarpPortableWordMath.MultiplyHigh(leftLow, rightHigh);
        sum = unchecked(upper + term);
        top = unchecked(top + (sum < upper ? 1u : 0u));
        upper = sum;
        term = WarpPortableWordMath.MultiplyHigh(leftHigh, rightLow);
        sum = unchecked(upper + term);
        top = unchecked(top + (sum < upper ? 1u : 0u));
        upper = sum;
        sum = unchecked(upper + carry);
        top = unchecked(top + (sum < upper ? 1u : 0u));
        return word == 0 ? sum : top;
    }

    private static uint DivideRemainder(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh, uint word)
    {
        if ((rightLow | rightHigh) == 0)
        {
            return 0;
        }

        uint quotientLow = 0;
        uint quotientHigh = 0;
        uint remainderLow = 0;
        uint remainderHigh = 0;
        for (uint bit = 0; bit < 64; bit++)
        {
            uint carry = remainderHigh >> 31;
            remainderHigh = remainderHigh << 1 | remainderLow >> 31;
            remainderLow = remainderLow << 1 | leftHigh >> 31;
            leftHigh = leftHigh << 1 | leftLow >> 31;
            leftLow <<= 1;
            quotientHigh = quotientHigh << 1 | quotientLow >> 31;
            quotientLow <<= 1;
            if (carry != 0 || LessThanUnsigned(remainderLow, remainderHigh, rightLow, rightHigh) == 0)
            {
                remainderHigh = SubtractHigh(remainderLow, remainderHigh, rightLow, rightHigh);
                remainderLow = unchecked(remainderLow - rightLow);
                quotientLow |= 1;
            }
        }

        return word == 0 ? quotientLow : word == 1 ? quotientHigh : word == 2 ? remainderLow : remainderHigh;
    }

    private static uint DivideRemainderSigned(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh, uint word)
    {
        if (DivideSignedFault(leftLow, leftHigh, rightLow, rightHigh) != WarpPortableIntegerFault.None)
        {
            return 0;
        }

        uint sign = word < 2 ? (leftHigh ^ rightHigh) >> 31 : leftHigh >> 31;
        if ((leftHigh >> 31) != 0)
        {
            leftHigh = NegateHigh(leftLow, leftHigh);
            leftLow = NegateLow(leftLow, leftHigh);
        }

        if ((rightHigh >> 31) != 0)
        {
            rightHigh = NegateHigh(rightLow, rightHigh);
            rightLow = NegateLow(rightLow, rightHigh);
        }

        uint low = DivideRemainder(leftLow, leftHigh, rightLow, rightHigh, word & 2u);
        if ((word & 1u) == 0)
        {
            return sign == 0 ? low : NegateLow(low, 0);
        }

        uint high = DivideRemainder(leftLow, leftHigh, rightLow, rightHigh, word);
        return sign == 0 ? high : NegateHigh(low, high);
    }
}
