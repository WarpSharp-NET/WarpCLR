using System.Runtime.InteropServices;

namespace WarpCLR.Tests;

internal static class WarpSourceEndKernels
{
    public static void Empty() { }
    public static uint EmptyCall(uint value) { Empty(); return value; }
    public static Triple ReturnTriple(uint value) => new() { First = value, Wide = 0x8123456789ABCDEF };
    public static ulong Catch(Exception error) { try { Throw(error); } catch (InvalidOperationException) { return 0x8123456789ABCDEF; } return 0; }
    public static uint Filter(Exception error, uint flag) { try { Throw(error); } catch (InvalidOperationException) when (flag != 0) { return 17; } return 0; }
    public static uint MultipleFilters(Exception error, uint flag)
    {
        try { Throw(error); }
        catch (InvalidOperationException) when (flag == 1) { return 11; }
        catch (InvalidOperationException) when (flag == 2) { return 22; }
        return 0;
    }
    public static uint Wait(uint milliseconds) { Thread.Sleep(unchecked((int)milliseconds)); return milliseconds; }
    private static void Throw(Exception error) => throw error;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct Triple
    {
        public uint First;
        public ulong Wide;
    }
}
