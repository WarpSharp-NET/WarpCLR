namespace WarpCLR.Compiler;

internal static class WarpPortableSourceMemoryLayout
{
    internal const string Semantics = "warp.source-byref/exact-byte-views-static-owner-readonly-covariance-nullable-copy-frame-array-shapes/0.4";
    public const uint Descriptor = 56;
    public const uint Magic = 0x57525356;
    public const uint Version = 4;
    public const uint HeaderWords = 32;
    public const uint TypeCount = 2;
    public const uint TypeStart = 3;
    public const uint ViewCount = 4;
    public const uint ViewStart = 5;
    public const uint NullableCount = 6;
    public const uint NullableStart = 7;
    public const uint Hash = 8;
    public const uint FrameCount = 16;
    public const uint FrameStart = 17;
    public const uint FrameViewCount = 18;
    public const uint FrameViewStart = 19;
    public const uint FrameHash = 24;
    public const uint FrameWords = 4;
    public const uint FrameFunction = 0;
    public const uint FramePrivateWords = 1;
    public const uint FrameViews = 2;
    public const uint FrameViewsCount = 3;
    public const uint FrameViewWords = 3;
    public const uint TypeWords = 7;
    public const uint MemoryBytes = 0;
    public const uint TypeKind = 1;
    public const uint StrideBytes = 2;
    public const uint ElementType = 3;
    public const uint ViewWords = 5;
    public const uint ViewOwnerType = 0;
    public const uint ViewOwnerKind = 1;
    public const uint ViewByteOffset = 2;
    public const uint ViewByteSpan = 3;
    public const uint ViewElementType = 4;
    public const uint Instance = 1;
    public const uint Static = 2;
    public const uint ArrayElement = 3;
    public const uint NullableWords = 4;
    public const uint NullableType = 0;
    public const uint NullableElementType = 1;
    public const uint NullableHasValueByte = 2;
    public const uint NullableValueByte = 3;
}
