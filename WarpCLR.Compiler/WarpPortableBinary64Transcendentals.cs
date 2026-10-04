namespace WarpCLR.Compiler;

internal static partial class WarpPortableBinary64Transcendentals
{
    internal const string Semantics = "warp.math.binary64.transcendentals/pht2048-phase192-taylor-ddlog-fma-approx-v1/0.1";

    public static uint SinLow(uint low, uint high) => Trigonometric(low, high, 0, 0);

    public static uint SinHigh(uint low, uint high) => Trigonometric(low, high, 0, 1);

    public static uint CosLow(uint low, uint high) => Trigonometric(low, high, 1, 0);

    public static uint CosHigh(uint low, uint high) => Trigonometric(low, high, 1, 1);

    public static uint TanLow(uint low, uint high) => Trigonometric(low, high, 2, 0);

    public static uint TanHigh(uint low, uint high) => Trigonometric(low, high, 2, 1);

    public static uint ExpLow(uint low, uint high) => Exponential(low, high, 0);

    public static uint ExpHigh(uint low, uint high) => Exponential(low, high, 1);

    public static uint LogLow(uint low, uint high) => Logarithm(low, high, 0, 0);

    public static uint LogHigh(uint low, uint high) => Logarithm(low, high, 0, 1);

    public static uint Log2Low(uint low, uint high) => Logarithm(low, high, 1, 0);

    public static uint Log2High(uint low, uint high) => Logarithm(low, high, 1, 1);

    public static uint Log10Low(uint low, uint high) => Logarithm(low, high, 2, 0);

    public static uint Log10High(uint low, uint high) => Logarithm(low, high, 2, 1);

    public static uint AtanLow(uint low, uint high) => ArcTangent(low, high, 0);

    public static uint AtanHigh(uint low, uint high) => ArcTangent(low, high, 1);

    public static uint AsinLow(uint low, uint high) => ArcSineCosine(low, high, 0, 0);

    public static uint AsinHigh(uint low, uint high) => ArcSineCosine(low, high, 0, 1);

    public static uint AcosLow(uint low, uint high) => ArcSineCosine(low, high, 1, 0);

    public static uint AcosHigh(uint low, uint high) => ArcSineCosine(low, high, 1, 1);

    public static uint SinhLow(uint low, uint high) => Hyperbolic(low, high, 0, 0);

    public static uint SinhHigh(uint low, uint high) => Hyperbolic(low, high, 0, 1);

    public static uint CoshLow(uint low, uint high) => Hyperbolic(low, high, 1, 0);

    public static uint CoshHigh(uint low, uint high) => Hyperbolic(low, high, 1, 1);

    public static uint TanhLow(uint low, uint high) => Hyperbolic(low, high, 2, 0);

    public static uint TanhHigh(uint low, uint high) => Hyperbolic(low, high, 2, 1);

    public static uint AsinhLow(uint low, uint high) => InverseHyperbolic(low, high, 0, 0);

    public static uint AsinhHigh(uint low, uint high) => InverseHyperbolic(low, high, 0, 1);

    public static uint AcoshLow(uint low, uint high) => InverseHyperbolic(low, high, 1, 0);

    public static uint AcoshHigh(uint low, uint high) => InverseHyperbolic(low, high, 1, 1);

    public static uint AtanhLow(uint low, uint high) => InverseHyperbolic(low, high, 2, 0);

    public static uint AtanhHigh(uint low, uint high) => InverseHyperbolic(low, high, 2, 1);

    public static uint CbrtLow(uint low, uint high) => CubeRoot(low, high, 0);

    public static uint CbrtHigh(uint low, uint high) => CubeRoot(low, high, 1);

    public static uint Atan2Low(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) => ArcTangent2(leftLow, leftHigh, rightLow, rightHigh, 0);

    public static uint Atan2High(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) => ArcTangent2(leftLow, leftHigh, rightLow, rightHigh, 1);

    public static uint PowLow(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) => Power(leftLow, leftHigh, rightLow, rightHigh, 0);

    public static uint PowHigh(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) => Power(leftLow, leftHigh, rightLow, rightHigh, 1);

    public static uint LogBaseLow(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) => LogarithmBase(leftLow, leftHigh, rightLow, rightHigh, 0);

    public static uint LogBaseHigh(uint leftLow, uint leftHigh, uint rightLow, uint rightHigh) => LogarithmBase(leftLow, leftHigh, rightLow, rightHigh, 1);

}
