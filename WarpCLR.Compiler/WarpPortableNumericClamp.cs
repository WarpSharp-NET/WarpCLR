namespace WarpCLR.Compiler;

internal static class WarpPortableNumericClamp
{
    internal const string Semantics = "warp.math.binary32-binary64.clamp/ordered-raw-selection-argument-fault/0.1";

    public static uint Binary32(uint value, uint minimum, uint maximum)
    {
        if (Binary32Fault(minimum, maximum) != 0) { return 0; }
        if (Less32(value, minimum) != 0) { return minimum; }
        return Less32(maximum, value) != 0 ? maximum : value;
    }

    public static uint Binary32Fault(uint minimum, uint maximum) => Less32(maximum, minimum) != 0 ? 3u : 0u;

    public static uint Binary64Low(uint valueLow, uint valueHigh, uint minimumLow, uint minimumHigh, uint maximumLow, uint maximumHigh) =>
        Select64(valueLow, valueHigh, minimumLow, minimumHigh, maximumLow, maximumHigh, 0);

    public static uint Binary64High(uint valueLow, uint valueHigh, uint minimumLow, uint minimumHigh, uint maximumLow, uint maximumHigh) =>
        Select64(valueLow, valueHigh, minimumLow, minimumHigh, maximumLow, maximumHigh, 1);

    public static uint Binary64Fault(uint minimumLow, uint minimumHigh, uint maximumLow, uint maximumHigh) =>
        Less64(maximumLow, maximumHigh, minimumLow, minimumHigh) != 0 ? 3u : 0u;

    private static uint Select64(uint valueLow, uint valueHigh, uint minimumLow, uint minimumHigh, uint maximumLow, uint maximumHigh, uint word)
    {
        if (Binary64Fault(minimumLow, minimumHigh, maximumLow, maximumHigh) != 0) { return 0; }
        if (Less64(valueLow, valueHigh, minimumLow, minimumHigh) != 0) { return word == 0 ? minimumLow : minimumHigh; }
        return Less64(maximumLow, maximumHigh, valueLow, valueHigh) != 0 ? word == 0 ? maximumLow : maximumHigh : word == 0 ? valueLow : valueHigh;
    }

    private static uint Less32(uint left, uint right)
    {
        uint leftMagnitude = left & 0x7FFFFFFFu;
        uint rightMagnitude = right & 0x7FFFFFFFu;
        if (leftMagnitude > 0x7F800000u || rightMagnitude > 0x7F800000u || (leftMagnitude | rightMagnitude) == 0) { return 0; }
        left = (left >> 31) != 0 ? ~left : left ^ 0x80000000u;
        right = (right >> 31) != 0 ? ~right : right ^ 0x80000000u;
        return left < right ? 1u : 0u;
    }

    private static uint Less64(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh)
    {
        uint leftMagnitude = leftHigh & 0x7FFFFFFFu;
        uint rightMagnitude = rightHigh & 0x7FFFFFFFu;
        if (IsNaN64(leftLow, leftMagnitude) != 0 || IsNaN64(rightLow, rightMagnitude) != 0 ||
            (leftLow | rightLow | leftMagnitude | rightMagnitude) == 0) { return 0; }
        if ((leftHigh >> 31) != 0) { leftLow = ~leftLow; leftHigh = ~leftHigh; }
        else { leftHigh ^= 0x80000000u; }
        if ((rightHigh >> 31) != 0) { rightLow = ~rightLow; rightHigh = ~rightHigh; }
        else { rightHigh ^= 0x80000000u; }
        return leftHigh < rightHigh || (leftHigh == rightHigh && leftLow < rightLow) ? 1u : 0u;
    }

    private static uint IsNaN64(uint low, uint high) => high > 0x7FF00000u || (high == 0x7FF00000u && low != 0) ? 1u : 0u;
}
