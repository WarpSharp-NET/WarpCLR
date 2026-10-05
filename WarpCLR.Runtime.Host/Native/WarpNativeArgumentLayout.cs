namespace WarpCLR.Runtime.Host.Native;

internal static class WarpNativeArgumentLayout
{
    public static ulong GetPackedBytes(int inputCount, int scalarCount, bool machine, bool managedArena = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inputCount);
        ArgumentOutOfRangeException.ThrowIfNegative(scalarCount);
        ulong pointers = checked(((ulong)inputCount + 1) * sizeof(ulong));
        ulong scalars = checked(((ulong)scalarCount + (machine ? 4UL : 1UL)) * sizeof(uint));
        if (managedArena && !machine) { throw new ArgumentException("Managed arena parameters require the logical-machine ABI.", nameof(managedArena)); }
        ulong bytes = pointers + scalars;
        if (managedArena) { bytes = checked(AlignPointer(bytes) + sizeof(ulong) + sizeof(uint)); }
        // The shared native parameter block has eight-byte pointer alignment, including final structure padding.
        return AlignPointer(bytes);
    }

    public static void Validate(WarpNativeTarget target, int inputCount, int scalarCount, bool machine, bool managedArena = false)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (GetPackedBytes(inputCount, scalarCount, machine, managedArena) > target.MaximumKernelArgumentBytes)
        {
            throw new WarpHostException("WRPNATIVE1005", "The portable kernel argument block exceeds the concrete target's admitted ABI capacity.");
        }
    }

    private static ulong AlignPointer(ulong bytes) => checked((bytes + sizeof(ulong) - 1) / sizeof(ulong) * sizeof(ulong));
}
