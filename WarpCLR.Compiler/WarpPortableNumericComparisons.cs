namespace WarpCLR.Compiler;

internal static class WarpPortableNumericComparisons
{
    internal const string Semantics = "warp.math.binary32-binary64.comparisons/ordered-canonical-nan-signed-zero/0.1";

    public static uint Binary32Unordered(uint left, uint right) =>
        IsBinary32NaN(left) != 0 || IsBinary32NaN(right) != 0 ? 1u : 0u;

    public static uint Binary32Equal(uint left, uint right)
    {
        if (Binary32Unordered(left, right) != 0)
        {
            return 0;
        }

        return left == right || ((left | right) & 0x7FFFFFFFu) == 0 ? 1u : 0u;
    }

    public static uint Binary32NotEqual(uint left, uint right) => Binary32Equal(left, right) ^ 1u;

    public static uint Binary32Less(uint left, uint right)
    {
        if (Binary32Unordered(left, right) != 0 || Binary32Equal(left, right) != 0)
        {
            return 0;
        }

        if (((left ^ right) & 0x80000000u) != 0)
        {
            return left >> 31;
        }

        return (left >> 31 == 0 ? left < right : left > right) ? 1u : 0u;
    }

    public static uint Binary32Greater(uint left, uint right) => Binary32Less(right, left);

    public static uint Binary32LessOrEqual(uint left, uint right) => Binary32Less(left, right) | Binary32Equal(left, right);

    public static uint Binary32GreaterOrEqual(uint left, uint right) => Binary32LessOrEqual(right, left);

    public static uint Binary32LessOrUnordered(uint left, uint right) => Binary32Less(left, right) | Binary32Unordered(left, right);

    public static uint Binary32GreaterOrUnordered(uint left, uint right) => Binary32LessOrUnordered(right, left);

    public static uint Binary32Negate(uint value) => IsBinary32NaN(value) != 0 ? 0x7FC00000u : value ^ 0x80000000u;

    public static uint Binary32Abs(uint value) => IsBinary32NaN(value) != 0 ? 0x7FC00000u : value & 0x7FFFFFFFu;

    public static uint Binary32Min(uint left, uint right)
    {
        if (Binary32Unordered(left, right) != 0)
        {
            return 0x7FC00000u;
        }

        if (((left | right) & 0x7FFFFFFFu) == 0)
        {
            return left | right;
        }

        return Binary32Less(right, left) != 0 ? right : left;
    }

    public static uint Binary32Max(uint left, uint right)
    {
        if (Binary32Unordered(left, right) != 0)
        {
            return 0x7FC00000u;
        }

        if (((left | right) & 0x7FFFFFFFu) == 0)
        {
            return left & right;
        }

        return Binary32Less(left, right) != 0 ? right : left;
    }

    public static uint Binary64Unordered(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        IsBinary64NaN(leftLow, leftHigh) != 0 || IsBinary64NaN(rightLow, rightHigh) != 0 ? 1u : 0u;

    public static uint Binary64Equal(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh)
    {
        if (Binary64Unordered(leftLow, leftHigh, rightLow, rightHigh) != 0)
        {
            return 0;
        }

        return (leftLow == rightLow && leftHigh == rightHigh) ||
            ((leftLow | rightLow | ((leftHigh | rightHigh) & 0x7FFFFFFFu)) == 0) ? 1u : 0u;
    }

    public static uint Binary64NotEqual(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        Binary64Equal(leftLow, leftHigh, rightLow, rightHigh) ^ 1u;

    public static uint Binary64Less(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh)
    {
        if (Binary64Unordered(leftLow, leftHigh, rightLow, rightHigh) != 0 ||
            Binary64Equal(leftLow, leftHigh, rightLow, rightHigh) != 0)
        {
            return 0;
        }

        if (((leftHigh ^ rightHigh) & 0x80000000u) != 0)
        {
            return leftHigh >> 31;
        }

        bool magnitudeLess = leftHigh < rightHigh || (leftHigh == rightHigh && leftLow < rightLow);
        return (leftHigh >> 31 == 0 ? magnitudeLess : !magnitudeLess) ? 1u : 0u;
    }

    public static uint Binary64Greater(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        Binary64Less(rightLow, rightHigh, leftLow, leftHigh);

    public static uint Binary64LessOrEqual(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        Binary64Less(leftLow, leftHigh, rightLow, rightHigh) | Binary64Equal(leftLow, leftHigh, rightLow, rightHigh);

    public static uint Binary64GreaterOrEqual(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        Binary64LessOrEqual(rightLow, rightHigh, leftLow, leftHigh);

    public static uint Binary64LessOrUnordered(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        Binary64Less(leftLow, leftHigh, rightLow, rightHigh) | Binary64Unordered(leftLow, leftHigh, rightLow, rightHigh);

    public static uint Binary64GreaterOrUnordered(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        Binary64LessOrUnordered(rightLow, rightHigh, leftLow, leftHigh);

    public static uint Binary64NegateLow(uint low, uint high) => IsBinary64NaN(low, high) != 0 ? 0 : low;

    public static uint Binary64NegateHigh(uint low, uint high) =>
        IsBinary64NaN(low, high) != 0 ? 0x7FF80000u : high ^ 0x80000000u;

    public static uint Binary64AbsLow(uint low, uint high) => IsBinary64NaN(low, high) != 0 ? 0 : low;

    public static uint Binary64AbsHigh(uint low, uint high) => IsBinary64NaN(low, high) != 0 ? 0x7FF80000u : high & 0x7FFFFFFFu;

    public static uint Binary64MinLow(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        Binary64Min(leftLow, leftHigh, rightLow, rightHigh, 0);

    public static uint Binary64MinHigh(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        Binary64Min(leftLow, leftHigh, rightLow, rightHigh, 1);

    public static uint Binary64MaxLow(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        Binary64Max(leftLow, leftHigh, rightLow, rightHigh, 0);

    public static uint Binary64MaxHigh(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        Binary64Max(leftLow, leftHigh, rightLow, rightHigh, 1);

    private static uint Binary64Min(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh, uint word)
    {
        if (Binary64Unordered(leftLow, leftHigh, rightLow, rightHigh) != 0)
        {
            return word == 0 ? 0 : 0x7FF80000u;
        }

        if ((leftLow | rightLow | ((leftHigh | rightHigh) & 0x7FFFFFFFu)) == 0)
        {
            return word == 0 ? 0 : leftHigh | rightHigh;
        }

        bool right = Binary64Less(rightLow, rightHigh, leftLow, leftHigh) != 0;
        return word == 0 ? (right ? rightLow : leftLow) : (right ? rightHigh : leftHigh);
    }

    private static uint Binary64Max(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh, uint word)
    {
        if (Binary64Unordered(leftLow, leftHigh, rightLow, rightHigh) != 0)
        {
            return word == 0 ? 0 : 0x7FF80000u;
        }

        if ((leftLow | rightLow | ((leftHigh | rightHigh) & 0x7FFFFFFFu)) == 0)
        {
            return word == 0 ? 0 : leftHigh & rightHigh;
        }

        bool right = Binary64Less(leftLow, leftHigh, rightLow, rightHigh) != 0;
        return word == 0 ? (right ? rightLow : leftLow) : (right ? rightHigh : leftHigh);
    }

    private static uint IsBinary32NaN(uint value) =>
        (value & 0x7F800000u) == 0x7F800000u && (value & 0x7FFFFFu) != 0 ? 1u : 0u;

    private static uint IsBinary64NaN(uint low, uint high) =>
        (high & 0x7FF00000u) == 0x7FF00000u && ((high & 0xFFFFFu) | low) != 0 ? 1u : 0u;
}
