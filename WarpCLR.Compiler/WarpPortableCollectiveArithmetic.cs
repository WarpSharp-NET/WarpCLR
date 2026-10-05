namespace WarpCLR.Compiler;

internal static class WarpPortableCollectiveArithmetic
{
    internal const string Semantics = "warp.collectives.arithmetic/same-type-strict-rne-gradual-canonical-nan-checked-or-modular/0.1";

    public static uint IdentityLow(uint type, uint operation)
    {
        if (operation == WarpPortableCollectiveLayout.Sum) { return 0; }
        if (operation == WarpPortableCollectiveLayout.Product)
        {
            return type == 5 ? 0x3F800000u : type == 6 ? 0u : 1u;
        }
        if (type == 5) { return operation == 2 ? 0x7F800000u : 0xFF800000u; }
        if (type == 6) { return 0; }
        if (type == 2) { return operation == 2 ? 0x7FFFFFFFu : 0x80000000u; }
        if (type == 4) { return operation == 2 ? 0xFFFFFFFFu : 0; }
        return operation == 2 ? 0xFFFFFFFFu : 0;
    }

    public static uint IdentityHigh(uint type, uint operation)
    {
        if (type == 6)
        {
            return operation == 1 ? 0 : operation == 4 ? 0x3FF00000u : operation == 2 ? 0x7FF00000u : 0xFFF00000u;
        }
        if (type == 4 && (operation == 2 || operation == 3))
        {
            return operation == 2 ? 0x7FFFFFFFu : 0x80000000u;
        }
        return type == 3 && operation == 2 ? 0xFFFFFFFFu : 0;
    }

    public static uint ApplyLow(uint type, uint operation, uint leftLow, uint leftHigh, uint rightLow, uint rightHigh)
    {
        if (operation == 2 || operation == 3)
        {
            return SelectLow(type, operation, leftLow, leftHigh, rightLow, rightHigh);
        }
        if (type == 5)
        {
            return operation == 1 ? WarpPortableBinary32.Add(leftLow, rightLow) : WarpPortableBinary32.Multiply(leftLow, rightLow);
        }
        if (type == 6)
        {
            return operation == 1 ? WarpPortableBinary64.AddLow(leftLow, leftHigh, rightLow, rightHigh) :
                WarpPortableBinary64.MultiplyLow(leftLow, leftHigh, rightLow, rightHigh);
        }
        return operation == 1 ? unchecked(leftLow + rightLow) : unchecked(leftLow * rightLow);
    }

    public static uint ApplyHigh(uint type, uint operation, uint leftLow, uint leftHigh, uint rightLow, uint rightHigh)
    {
        if (type == 1 || type == 2 || type == 5) { return 0; }
        if (operation == 2 || operation == 3)
        {
            return SelectHigh(type, operation, leftLow, leftHigh, rightLow, rightHigh);
        }
        if (type == 6)
        {
            return operation == 1 ? WarpPortableBinary64.AddHigh(leftLow, leftHigh, rightLow, rightHigh) :
                WarpPortableBinary64.MultiplyHigh(leftLow, leftHigh, rightLow, rightHigh);
        }
        return operation == 1 ? WarpPortableInteger64.AddHigh(leftLow, leftHigh, rightLow, rightHigh) :
            WarpPortableInteger64.MultiplyHigh(leftLow, leftHigh, rightLow, rightHigh);
    }

    public static uint ApplyFault(uint type, uint operation, uint overflow, uint leftLow, uint leftHigh, uint rightLow, uint rightHigh)
    {
        if (overflow == 0 || operation == 2 || operation == 3 || type >= 5) { return 0; }
        if (operation == 1)
        {
            return AddFault(type, leftLow, leftHigh, rightLow, rightHigh);
        }
        if (type == 1) { return WarpPortableInteger32.MultiplyUnsignedFault(leftLow, rightLow); }
        if (type == 2) { return WarpPortableInteger32.MultiplySignedFault(leftLow, rightLow); }
        return type == 3 ? WarpPortableInteger64.MultiplyUnsignedFault(leftLow, leftHigh, rightLow, rightHigh) :
            WarpPortableInteger64.MultiplySignedFault(leftLow, leftHigh, rightLow, rightHigh);
    }

    private static uint AddFault(uint type, uint leftLow, uint leftHigh, uint rightLow, uint rightHigh)
    {
        if (type == 1) { return WarpPortableInteger32.AddUnsignedFault(leftLow, rightLow); }
        if (type == 2) { return WarpPortableInteger32.AddSignedFault(leftLow, rightLow); }
        return type == 3 ? WarpPortableInteger64.AddUnsignedFault(leftLow, leftHigh, rightLow, rightHigh) :
            WarpPortableInteger64.AddSignedFault(leftLow, leftHigh, rightLow, rightHigh);
    }

    private static uint SelectRight(uint type, uint operation, uint leftLow, uint leftHigh, uint rightLow, uint rightHigh)
    {
        uint less = type == 1 ? WarpPortableInteger32.LessThanUnsigned(leftLow, rightLow) :
            type == 2 ? WarpPortableInteger32.LessThanSigned(leftLow, rightLow) :
            type == 3 ? WarpPortableInteger64.LessThanUnsigned(leftLow, leftHigh, rightLow, rightHigh) :
            WarpPortableInteger64.LessThanSigned(leftLow, leftHigh, rightLow, rightHigh);
        if (operation == 3) { return less; }
        return (leftLow != rightLow || leftHigh != rightHigh) && less == 0 ? 1u : 0u;
    }

    private static uint SelectLow(uint type, uint operation, uint leftLow, uint leftHigh, uint rightLow, uint rightHigh)
    {
        if (type == 5)
        {
            return operation == 2 ? WarpPortableNumericComparisons.Binary32Min(leftLow, rightLow) :
                WarpPortableNumericComparisons.Binary32Max(leftLow, rightLow);
        }
        if (type == 6)
        {
            return operation == 2 ? WarpPortableNumericComparisons.Binary64MinLow(leftLow, leftHigh, rightLow, rightHigh) :
                WarpPortableNumericComparisons.Binary64MaxLow(leftLow, leftHigh, rightLow, rightHigh);
        }
        return SelectRight(type, operation, leftLow, leftHigh, rightLow, rightHigh) != 0 ? rightLow : leftLow;
    }

    private static uint SelectHigh(uint type, uint operation, uint leftLow, uint leftHigh, uint rightLow, uint rightHigh)
    {
        if (type == 6)
        {
            return operation == 2 ? WarpPortableNumericComparisons.Binary64MinHigh(leftLow, leftHigh, rightLow, rightHigh) :
                WarpPortableNumericComparisons.Binary64MaxHigh(leftLow, leftHigh, rightLow, rightHigh);
        }
        return SelectRight(type, operation, leftLow, leftHigh, rightLow, rightHigh) != 0 ? rightHigh : leftHigh;
    }
}
