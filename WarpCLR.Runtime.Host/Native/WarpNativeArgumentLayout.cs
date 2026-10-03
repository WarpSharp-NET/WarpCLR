namespace WarpCLR.Runtime.Host.Native;

internal static class WarpNativeArgumentLayout
{
    public static ulong GetPackedBytes(int inputCount, int scalarCount, bool machine)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inputCount);
        ArgumentOutOfRangeException.ThrowIfNegative(scalarCount);
        ulong pointers = checked(((ulong)inputCount + 1) * sizeof(ulong));
        ulong scalars = checked(((ulong)scalarCount + (machine ? 4UL : 1UL)) * sizeof(uint));
        // The shared native parameter block has eight-byte pointer alignment, including final structure padding.
        return checked((pointers + scalars + sizeof(ulong) - 1) / sizeof(ulong) * sizeof(ulong));
    }

    public static void Validate(WarpNativeTarget target, int inputCount, int scalarCount, bool machine)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (GetPackedBytes(inputCount, scalarCount, machine) > target.MaximumKernelArgumentBytes)
        {
            throw new WarpHostException("WRPNATIVE1005", "The portable kernel argument block exceeds the concrete target's admitted ABI capacity.");
        }
    }
}
