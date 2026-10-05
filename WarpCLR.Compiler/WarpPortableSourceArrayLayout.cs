namespace WarpCLR.Compiler;

internal static class WarpPortableSourceArrayLayout
{
    internal const string Semantics = "warp.source-array/typed-rank-signed-lower-bounds-clr-fault-order-vector-morph-row-major-generation-shapes/0.2";
    public const uint ShapeStart = 20;
    public const uint ShapeCount = 21;
    public const uint ShapeStride = 22;
    public const uint MaximumRank = 32;
    public const uint ShapeWords = 4 + MaximumRank * 2;
    public const uint Generation = 0;
    public const uint Type = 1;
    public const uint Rank = 2;
    public const uint Vector = 3;
    public const uint Dimensions = 4;
    public const uint TypeRank = 4;
    public const uint TypeVector = 5;
    public const uint TypeVectorIdentity = 6;
    public const uint MaximumDimensionLength = 0x7FFFFFC7;
    public const uint ArithmeticOverflow = 12;
    public const uint ArgumentOutOfRange = 13;
    public const uint ManagedDimensionsExceeded = 14;
}
