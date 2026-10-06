using System.Runtime.InteropServices;

namespace WarpCLR.Tests;

internal static class WarpSourceEventKernels
{
    public static uint Invoke(uint value) => Guest(value) + 1;
    private static uint Guest(uint value) => value ^ 0xA5A5A5A5;

    public static uint Indirect(uint value)
    {
        uint local = value; Set(ref local); return local;
    }
    private static void Set(ref uint value) => value += 17;

    public static ulong Packed(uint value)
    {
        var record = new PackedValue { Tag = 0xA5, Half = 0xBEEF, Value = value, Tail = 0xDEADBEEF };
        return record.Value + record.Tag + record.Half + record.Tail;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct PackedValue { internal byte Tag; internal ushort Half; internal ulong Value; internal uint Tail; }
}
