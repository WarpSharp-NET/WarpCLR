namespace WarpCLR.Compiler;

internal static class WarpPortableIntegerMath
{
    internal const string Semantics = "warp.integer-math/fixed8-16-32-64-checked-abs-signed-order-clamp/0.1";

    public static uint Signed32Abs(uint value, uint width) => value >> 31 != 0 ? WarpPortableInteger32.Negate(value) : value;
    public static uint Signed32AbsFault(uint value, uint width) =>
        value == unchecked(0u - (1u << (int)(width - 1))) ? WarpPortableIntegerFault.Overflow : WarpPortableIntegerFault.None;
    public static uint Signed64AbsLow(uint low, uint high) => high >> 31 != 0 ? WarpPortableInteger64.NegateLow(low, high) : low;
    public static uint Signed64AbsHigh(uint low, uint high) => high >> 31 != 0 ? WarpPortableInteger64.NegateHigh(low, high) : high;
    public static uint Signed64AbsFault(uint low, uint high) => WarpPortableInteger64.NegateSignedFault(low, high);

    public static uint Signed32Sign(uint value) => value == 0 ? 0u : value >> 31 != 0 ? uint.MaxValue : 1u;
    public static uint Signed64Sign(uint low, uint high) => (low | high) == 0 ? 0u : high >> 31 != 0 ? uint.MaxValue : 1u;

    public static uint Signed32Min(uint left, uint right) => WarpPortableInteger32.LessThanSigned(left, right) != 0 ? left : right;
    public static uint Signed32Max(uint left, uint right) => WarpPortableInteger32.LessThanSigned(left, right) != 0 ? right : left;
    public static uint Unsigned32Min(uint left, uint right) => left < right ? left : right;
    public static uint Unsigned32Max(uint left, uint right) => left < right ? right : left;

    public static uint Signed64MinLow(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        WarpPortableInteger64.LessThanSigned(leftLow, leftHigh, rightLow, rightHigh) != 0 ? leftLow : rightLow;
    public static uint Signed64MinHigh(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        WarpPortableInteger64.LessThanSigned(leftLow, leftHigh, rightLow, rightHigh) != 0 ? leftHigh : rightHigh;
    public static uint Signed64MaxLow(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        WarpPortableInteger64.LessThanSigned(leftLow, leftHigh, rightLow, rightHigh) != 0 ? rightLow : leftLow;
    public static uint Signed64MaxHigh(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        WarpPortableInteger64.LessThanSigned(leftLow, leftHigh, rightLow, rightHigh) != 0 ? rightHigh : leftHigh;
    public static uint Unsigned64MinLow(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        WarpPortableInteger64.LessThanUnsigned(leftLow, leftHigh, rightLow, rightHigh) != 0 ? leftLow : rightLow;
    public static uint Unsigned64MinHigh(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        WarpPortableInteger64.LessThanUnsigned(leftLow, leftHigh, rightLow, rightHigh) != 0 ? leftHigh : rightHigh;
    public static uint Unsigned64MaxLow(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        WarpPortableInteger64.LessThanUnsigned(leftLow, leftHigh, rightLow, rightHigh) != 0 ? rightLow : leftLow;
    public static uint Unsigned64MaxHigh(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) =>
        WarpPortableInteger64.LessThanUnsigned(leftLow, leftHigh, rightLow, rightHigh) != 0 ? rightHigh : leftHigh;

    public static uint Signed32Clamp(uint value, uint minimum, uint maximum) =>
        Signed32ClampFault(minimum, maximum) != 0 ? 0 : Signed32Min(Signed32Max(value, minimum), maximum);
    public static uint Signed32ClampFault(uint minimum, uint maximum) => WarpPortableInteger32.LessThanSigned(maximum, minimum) != 0 ? 3u : 0u;
    public static uint Unsigned32Clamp(uint value, uint minimum, uint maximum) =>
        Unsigned32ClampFault(minimum, maximum) != 0 ? 0 : Unsigned32Min(Unsigned32Max(value, minimum), maximum);
    public static uint Unsigned32ClampFault(uint minimum, uint maximum) => maximum < minimum ? 3u : 0u;

    public static uint Signed64ClampLow(uint valueLow, uint valueHigh, uint minimumLow, uint minimumHigh, uint maximumLow, uint maximumHigh) =>
        Signed64ClampFault(minimumLow, minimumHigh, maximumLow, maximumHigh) != 0 ? 0 :
        WarpPortableInteger64.LessThanSigned(valueLow, valueHigh, minimumLow, minimumHigh) != 0 ? minimumLow :
        WarpPortableInteger64.LessThanSigned(maximumLow, maximumHigh, valueLow, valueHigh) != 0 ? maximumLow : valueLow;
    public static uint Signed64ClampHigh(uint valueLow, uint valueHigh, uint minimumLow, uint minimumHigh, uint maximumLow, uint maximumHigh) =>
        Signed64ClampFault(minimumLow, minimumHigh, maximumLow, maximumHigh) != 0 ? 0 :
        WarpPortableInteger64.LessThanSigned(valueLow, valueHigh, minimumLow, minimumHigh) != 0 ? minimumHigh :
        WarpPortableInteger64.LessThanSigned(maximumLow, maximumHigh, valueLow, valueHigh) != 0 ? maximumHigh : valueHigh;
    public static uint Signed64ClampFault(uint minimumLow, uint minimumHigh, uint maximumLow, uint maximumHigh) =>
        WarpPortableInteger64.LessThanSigned(maximumLow, maximumHigh, minimumLow, minimumHigh) != 0 ? 3u : 0u;
    public static uint Unsigned64ClampLow(uint valueLow, uint valueHigh, uint minimumLow, uint minimumHigh, uint maximumLow, uint maximumHigh) =>
        Unsigned64ClampFault(minimumLow, minimumHigh, maximumLow, maximumHigh) != 0 ? 0 :
        WarpPortableInteger64.LessThanUnsigned(valueLow, valueHigh, minimumLow, minimumHigh) != 0 ? minimumLow :
        WarpPortableInteger64.LessThanUnsigned(maximumLow, maximumHigh, valueLow, valueHigh) != 0 ? maximumLow : valueLow;
    public static uint Unsigned64ClampHigh(uint valueLow, uint valueHigh, uint minimumLow, uint minimumHigh, uint maximumLow, uint maximumHigh) =>
        Unsigned64ClampFault(minimumLow, minimumHigh, maximumLow, maximumHigh) != 0 ? 0 :
        WarpPortableInteger64.LessThanUnsigned(valueLow, valueHigh, minimumLow, minimumHigh) != 0 ? minimumHigh :
        WarpPortableInteger64.LessThanUnsigned(maximumLow, maximumHigh, valueLow, valueHigh) != 0 ? maximumHigh : valueHigh;
    public static uint Unsigned64ClampFault(uint minimumLow, uint minimumHigh, uint maximumLow, uint maximumHigh) =>
        WarpPortableInteger64.LessThanUnsigned(maximumLow, maximumHigh, minimumLow, minimumHigh) != 0 ? 3u : 0u;
}
