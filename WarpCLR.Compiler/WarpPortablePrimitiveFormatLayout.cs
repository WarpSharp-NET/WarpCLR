namespace WarpCLR.Compiler;

internal static class WarpPortablePrimitiveFormatLayout
{
    internal const string Semantics = "warp.primitive-format/coreclr10.0.12-G-raw-single-double-exact-interval-plus-two-pinned-double-fallbacks-captured-utf16-symbols/0.1";
    internal const string CultureSemantics = "warp.numeric-format-culture/exact-immutable-six-raw-utf16-code-unit-symbols/0.2";
    internal const string CoreClrFallbackCorpusSha256 = "98a630e7f39b8c6fb3ebe683a9a8d63d15e71a565adeaf366b36e83964f71ae3";
    internal const uint Magic = 0x4E465431;
    internal const uint ContractHeader = 24;
    internal const uint SymbolLimit = 256;
    internal const uint ScratchWords = 1312;
    internal const uint LimbWords = 40;
    internal const uint Value = 16;
    internal const uint Scale = 56;
    internal const uint MarginLow = 96;
    internal const uint MarginHigh = 136;
    internal const uint Temporary = 176;
    internal const uint Doubled = 216;
    internal const uint Digits = 256;
    internal const uint Render = 288;
    internal const uint RenderWords = 1024;
    internal const uint Int8 = 0;
    internal const uint UInt8 = 1;
    internal const uint Int16 = 2;
    internal const uint UInt16 = 3;
    internal const uint Int32 = 4;
    internal const uint UInt32 = 5;
    internal const uint Int64 = 6;
    internal const uint UInt64 = 7;
    internal const uint Single = 8;
    internal const uint Double = 9;
    internal const uint Character = 10;
    internal const uint Boolean = 11;
    internal const uint BadShape = 1;
    internal const uint BadKind = 2;
    internal const uint Capacity = 3;
    internal const uint ArithmeticCapacity = 4;
}
