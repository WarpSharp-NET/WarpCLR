namespace WarpCLR.Compiler;

internal static class WarpPortableExceptionTraceLayout
{
    internal const string Semantics = "warp.exception.logical-trace/disjoint-original-context-and-catch-propagation-activation-bound-alias-owner-site/0.2";
    public const uint Version = 2;
    public const uint HeaderWords = 12;
    public const uint FrameWords = 4;
    public const uint Identity = 0;
    public const uint Count = 1;
    public const uint ThrowMethod = 2;
    public const uint ThrowOffset = 3;
    public const uint FormatVersion = 4;
    public const uint RawCount = 5;
    public const uint RawStart = 6;
    public const uint ManagedStart = 7;
    public const uint Capacity = 8;
    public const uint ProjectionState = 9;
    public const uint FirstRawFrame = 10;
    public const uint Pending = 0;
    public const uint Projected = 1;
    public const uint Method = 0;
    public const uint Offset = 1;
    public const uint Physical = 2;
    public const uint Activation = 3;
}
