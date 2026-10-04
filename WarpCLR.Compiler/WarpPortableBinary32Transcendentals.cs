namespace WarpCLR.Compiler;

internal static class WarpPortableBinary32Transcendentals
{
    internal const string Semantics = "warp.math.binary32.transcendentals/eval64-pht2048-phase192-taylor-ddlog-fma-final-rne-approx-v1/0.1";

    public static uint Sin(uint value) => WarpPortableNumericConversions.DoubleToSingle(
        WarpPortableBinary64Transcendentals.SinLow(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)),
        WarpPortableBinary64Transcendentals.SinHigh(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)));

    public static uint Cos(uint value) => WarpPortableNumericConversions.DoubleToSingle(
        WarpPortableBinary64Transcendentals.CosLow(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)),
        WarpPortableBinary64Transcendentals.CosHigh(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)));

    public static uint Tan(uint value) => WarpPortableNumericConversions.DoubleToSingle(
        WarpPortableBinary64Transcendentals.TanLow(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)),
        WarpPortableBinary64Transcendentals.TanHigh(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)));

    public static uint Exp(uint value) => WarpPortableNumericConversions.DoubleToSingle(
        WarpPortableBinary64Transcendentals.ExpLow(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)),
        WarpPortableBinary64Transcendentals.ExpHigh(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)));

    public static uint Log(uint value) => WarpPortableNumericConversions.DoubleToSingle(
        WarpPortableBinary64Transcendentals.LogLow(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)),
        WarpPortableBinary64Transcendentals.LogHigh(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)));

    public static uint Log2(uint value) => WarpPortableNumericConversions.DoubleToSingle(
        WarpPortableBinary64Transcendentals.Log2Low(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)),
        WarpPortableBinary64Transcendentals.Log2High(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)));

    public static uint Log10(uint value) => WarpPortableNumericConversions.DoubleToSingle(
        WarpPortableBinary64Transcendentals.Log10Low(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)),
        WarpPortableBinary64Transcendentals.Log10High(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)));

    public static uint Atan(uint value) => WarpPortableNumericConversions.DoubleToSingle(
        WarpPortableBinary64Transcendentals.AtanLow(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)),
        WarpPortableBinary64Transcendentals.AtanHigh(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)));

    public static uint Asin(uint value) => WarpPortableNumericConversions.DoubleToSingle(
        WarpPortableBinary64Transcendentals.AsinLow(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)),
        WarpPortableBinary64Transcendentals.AsinHigh(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)));

    public static uint Acos(uint value) => WarpPortableNumericConversions.DoubleToSingle(
        WarpPortableBinary64Transcendentals.AcosLow(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)),
        WarpPortableBinary64Transcendentals.AcosHigh(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)));

    public static uint Sinh(uint value) => WarpPortableNumericConversions.DoubleToSingle(
        WarpPortableBinary64Transcendentals.SinhLow(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)),
        WarpPortableBinary64Transcendentals.SinhHigh(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)));

    public static uint Cosh(uint value) => WarpPortableNumericConversions.DoubleToSingle(
        WarpPortableBinary64Transcendentals.CoshLow(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)),
        WarpPortableBinary64Transcendentals.CoshHigh(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)));

    public static uint Tanh(uint value) => WarpPortableNumericConversions.DoubleToSingle(
        WarpPortableBinary64Transcendentals.TanhLow(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)),
        WarpPortableBinary64Transcendentals.TanhHigh(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)));

    public static uint Asinh(uint value) => WarpPortableNumericConversions.DoubleToSingle(
        WarpPortableBinary64Transcendentals.AsinhLow(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)),
        WarpPortableBinary64Transcendentals.AsinhHigh(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)));

    public static uint Acosh(uint value) => WarpPortableNumericConversions.DoubleToSingle(
        WarpPortableBinary64Transcendentals.AcoshLow(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)),
        WarpPortableBinary64Transcendentals.AcoshHigh(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)));

    public static uint Atanh(uint value) => WarpPortableNumericConversions.DoubleToSingle(
        WarpPortableBinary64Transcendentals.AtanhLow(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)),
        WarpPortableBinary64Transcendentals.AtanhHigh(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)));

    public static uint Cbrt(uint value) => WarpPortableNumericConversions.DoubleToSingle(
        WarpPortableBinary64Transcendentals.CbrtLow(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)),
        WarpPortableBinary64Transcendentals.CbrtHigh(WarpPortableNumericConversions.SingleToDoubleLow(value), WarpPortableNumericConversions.SingleToDoubleHigh(value)));

    public static uint Atan2(uint left, uint right) => WarpPortableNumericConversions.DoubleToSingle(
        WarpPortableBinary64Transcendentals.Atan2Low(WarpPortableNumericConversions.SingleToDoubleLow(left), WarpPortableNumericConversions.SingleToDoubleHigh(left),
            WarpPortableNumericConversions.SingleToDoubleLow(right), WarpPortableNumericConversions.SingleToDoubleHigh(right)),
        WarpPortableBinary64Transcendentals.Atan2High(WarpPortableNumericConversions.SingleToDoubleLow(left), WarpPortableNumericConversions.SingleToDoubleHigh(left),
            WarpPortableNumericConversions.SingleToDoubleLow(right), WarpPortableNumericConversions.SingleToDoubleHigh(right)));

    public static uint Pow(uint left, uint right) => WarpPortableNumericConversions.DoubleToSingle(
        WarpPortableBinary64Transcendentals.PowLow(WarpPortableNumericConversions.SingleToDoubleLow(left), WarpPortableNumericConversions.SingleToDoubleHigh(left),
            WarpPortableNumericConversions.SingleToDoubleLow(right), WarpPortableNumericConversions.SingleToDoubleHigh(right)),
        WarpPortableBinary64Transcendentals.PowHigh(WarpPortableNumericConversions.SingleToDoubleLow(left), WarpPortableNumericConversions.SingleToDoubleHigh(left),
            WarpPortableNumericConversions.SingleToDoubleLow(right), WarpPortableNumericConversions.SingleToDoubleHigh(right)));

    public static uint LogBase(uint left, uint right) => WarpPortableNumericConversions.DoubleToSingle(
        WarpPortableBinary64Transcendentals.LogBaseLow(WarpPortableNumericConversions.SingleToDoubleLow(left), WarpPortableNumericConversions.SingleToDoubleHigh(left),
            WarpPortableNumericConversions.SingleToDoubleLow(right), WarpPortableNumericConversions.SingleToDoubleHigh(right)),
        WarpPortableBinary64Transcendentals.LogBaseHigh(WarpPortableNumericConversions.SingleToDoubleLow(left), WarpPortableNumericConversions.SingleToDoubleHigh(left),
            WarpPortableNumericConversions.SingleToDoubleLow(right), WarpPortableNumericConversions.SingleToDoubleHigh(right)));

}
