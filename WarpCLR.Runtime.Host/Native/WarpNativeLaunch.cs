using System.Runtime.InteropServices;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host.Native;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct WarpNativeLaunch(uint GridX, uint WorkgroupSize, int OutputCount, ulong BytesRequired)
{
    public static WarpNativeLaunch Admit(
        WarpNativeTarget target,
        IReadOnlyList<uint[]> inputs,
        int itemCount,
        bool reduction,
        int scalarArgumentCount = 0)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentOutOfRangeException.ThrowIfNegative(itemCount);
        WarpNativeArgumentLayout.Validate(target, inputs.Count, scalarArgumentCount, machine: false);
        foreach (uint[]? input in inputs)
        {
            if (input is null || input.Length != itemCount)
            {
                throw new WarpHostException("WRPNATIVE1004", "Each input must match the dispatch item count.");
            }
        }

        uint workgroup = WarpDeviceAbi.IntegerMapWorkgroupSize;
        ulong grid = reduction ? 1UL : ((ulong)itemCount + workgroup - 1) / workgroup;
        int outputCount = reduction ? 1 : itemCount;
        // Zero-length arguments still require non-null pointers on driver APIs.
        ulong bytes = checked(((ulong)inputs.Count * (ulong)Math.Max(itemCount, 1) +
            (ulong)Math.Max(outputCount, 1)) * sizeof(uint));
        if (workgroup > target.MaxWorkgroupSize || grid > target.MaxGridX || bytes > target.GlobalMemoryBytes)
        {
            throw new WarpHostException("WRPNATIVE1005", "The dispatch exceeds the concrete device resource limits.");
        }

        return new WarpNativeLaunch((uint)grid, workgroup, outputCount, bytes);
    }
}
