namespace WarpCLR.Compiler;

internal static partial class WarpPortableInteger64
{
    internal const string Semantics = "warp.integer64/two-word-wrap-checked-divrem/0.1";

    public static uint AddLow(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) => unchecked(leftLow + rightLow);

    public static uint AddHigh(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        unchecked(leftHigh + rightHigh + (AddLow(leftLow, leftHigh, rightLow, rightHigh) < leftLow ? 1u : 0u));

    public static uint SubtractLow(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) => unchecked(leftLow - rightLow);

    public static uint SubtractHigh(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        unchecked(leftHigh - rightHigh - (leftLow < rightLow ? 1u : 0u));

    public static uint MultiplyLow(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) => unchecked(leftLow * rightLow);

    public static uint MultiplyHigh(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        unchecked(WarpPortableWordMath.MultiplyHigh(leftLow, rightLow) + leftLow * rightHigh + leftHigh * rightLow);

    public static uint NegateLow(uint low, uint high) => unchecked(0u - low);

    public static uint NegateHigh(uint low, uint high) => unchecked(0u - high - (low != 0 ? 1u : 0u));

    public static uint Equal(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        leftLow == rightLow && leftHigh == rightHigh ? 1u : 0u;

    public static uint LessThanUnsigned(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        leftHigh < rightHigh || (leftHigh == rightHigh && leftLow < rightLow) ? 1u : 0u;

    public static uint LessThanSigned(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        (leftHigh ^ 0x80000000u) < (rightHigh ^ 0x80000000u) ||
        (leftHigh == rightHigh && leftLow < rightLow) ? 1u : 0u;

    public static uint ShiftLeftLow(uint low, uint high, uint distance)
    {
        distance &= 63u;
        return distance < 32 ? low << (int)distance : 0;
    }

    public static uint ShiftLeftHigh(uint low, uint high, uint distance)
    {
        distance &= 63u;
        if (distance == 0)
        {
            return high;
        }

        return distance < 32 ? high << (int)distance | low >> (int)(32 - distance) : low << (int)(distance - 32);
    }

    public static uint ShiftRightUnsignedLow(uint low, uint high, uint distance)
    {
        distance &= 63u;
        if (distance == 0)
        {
            return low;
        }

        return distance < 32 ? low >> (int)distance | high << (int)(32 - distance) : high >> (int)(distance - 32);
    }

    public static uint ShiftRightUnsignedHigh(uint low, uint high, uint distance)
    {
        distance &= 63u;
        return distance < 32 ? high >> (int)distance : 0;
    }

    public static uint ShiftRightSignedLow(uint low, uint high, uint distance)
    {
        distance &= 63u;
        return distance < 32 ? ShiftRightUnsignedLow(low, high, distance) : WarpPortableInteger32.ShiftRightSigned(high, distance - 32);
    }

    public static uint ShiftRightSignedHigh(uint low, uint high, uint distance)
    {
        distance &= 63u;
        return distance < 32 ? WarpPortableInteger32.ShiftRightSigned(high, distance) : WarpPortableInteger32.ExtendSignedHigh(high);
    }

    public static uint AddUnsignedFault(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        LessThanUnsigned(AddLow(leftLow, leftHigh, rightLow, rightHigh), AddHigh(leftLow, leftHigh, rightLow, rightHigh), leftLow, leftHigh) != 0 ?
            WarpPortableIntegerFault.Overflow : WarpPortableIntegerFault.None;

    public static uint AddSignedFault(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh)
    {
        uint resultHigh = AddHigh(leftLow, leftHigh, rightLow, rightHigh);
        return (((leftHigh ^ resultHigh) & (rightHigh ^ resultHigh)) >> 31) != 0 ?
            WarpPortableIntegerFault.Overflow : WarpPortableIntegerFault.None;
    }

    public static uint SubtractUnsignedFault(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        LessThanUnsigned(leftLow, leftHigh, rightLow, rightHigh) != 0 ? WarpPortableIntegerFault.Overflow : WarpPortableIntegerFault.None;

    public static uint SubtractSignedFault(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        (((leftHigh ^ rightHigh) & (leftHigh ^ SubtractHigh(leftLow, leftHigh, rightLow, rightHigh))) >> 31) != 0 ?
            WarpPortableIntegerFault.Overflow : WarpPortableIntegerFault.None;

    public static uint NegateSignedFault(uint low, uint high) =>
        low == 0 && high == 0x80000000u ? WarpPortableIntegerFault.Overflow : WarpPortableIntegerFault.None;
}
