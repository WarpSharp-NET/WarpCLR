namespace WarpCLR.Compiler;

internal static class WarpPortableBinary32Math
{
    internal const string Semantics = "warp.math.binary32.sqrt-rem-ieeerem-fma/rne-gradual-single-round-canonical-nan/0.1";

    public static uint Sqrt(uint value) => WarpPortableBinary64Math.SqrtSingle(value);

    public static uint Remainder(uint left, uint right) => WarpPortableBinary64Math.RemainderSingle(left, right, 0);

    public static uint IeeeRemainder(uint left, uint right) => WarpPortableBinary64Math.RemainderSingle(left, right, 1);

    public static uint FusedMultiplyAdd(uint left, uint right, uint addend) =>
        WarpPortableBinary64Math.FusedMultiplyAddSingle(left, right, addend);
}
