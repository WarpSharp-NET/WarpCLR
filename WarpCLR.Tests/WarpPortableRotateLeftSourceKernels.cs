using System.Numerics;

namespace WarpCLR.Tests.Production;

internal static class WarpPortableRotateLeftSourceKernels
{
    public static uint Rotate32(uint value, int count) => BitOperations.RotateLeft(value, count);

    public static ulong Rotate64(ulong value, int count) => BitOperations.RotateLeft(value, count);

    public static float Transport32(float value, int count) =>
        BitConverter.UInt32BitsToSingle(BitOperations.RotateLeft(BitConverter.SingleToUInt32Bits(value), count));

    public static double Transport64(double value, int count) =>
        BitConverter.UInt64BitsToDouble(BitOperations.RotateLeft(BitConverter.DoubleToUInt64Bits(value), count));

    public static uint UnboundRotateRight32(uint value, int count) => BitOperations.RotateRight(value, count);

    public static ulong UnboundRotateRight64(ulong value, int count) => BitOperations.RotateRight(value, count);
}
