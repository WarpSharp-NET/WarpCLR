using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static class WarpPortableBitOperations
{
    internal const string Semantics = WarpPortableMethodGraphIntrinsics.RotateLeftContract;

    public static uint RotateLeft32(uint value, uint rawSignedCount)
    {
        uint distance = rawSignedCount & 31u;
        uint complement = unchecked(0u - distance) & 31u;
        return value << (int)distance | value >> (int)complement;
    }

    public static uint RotateLeft64Low(uint low, uint high, uint rawSignedCount)
    {
        uint distance = rawSignedCount & 63u;
        return WarpPortableInteger64.ShiftLeftLow(low, high, distance) |
            WarpPortableInteger64.ShiftRightUnsignedLow(low, high, unchecked(0u - distance));
    }

    public static uint RotateLeft64High(uint low, uint high, uint rawSignedCount)
    {
        uint distance = rawSignedCount & 63u;
        return WarpPortableInteger64.ShiftLeftHigh(low, high, distance) |
            WarpPortableInteger64.ShiftRightUnsignedHigh(low, high, unchecked(0u - distance));
    }
}
