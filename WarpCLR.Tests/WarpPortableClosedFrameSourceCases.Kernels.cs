using System.Runtime.InteropServices;

namespace WarpCLR.Tests.Production;

internal static partial class WarpPortableClosedFrameSourceCases
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Packed
    {
        public byte Tag;
        public short Signed;
        public ulong Wide;
        public float Number;
        public byte Tail;
        public Packed(byte tag, short signed, ulong wide, float number, byte tail)
        { Tag = tag; Signed = signed; Wide = wide; Number = number; Tail = tail; }
        public readonly ulong Project() => Wide ^ ((ulong)Tag << 56);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BoolPair
    {
        public bool Flag;
        public byte Tail;
        public BoolPair(bool flag, byte tail) { Flag = flag; Tail = tail; }
    }

    private struct WithReference
    {
        public byte Tag;
        public object? Reference;
        public ulong Wide;
        public WithReference(byte tag, object? reference, ulong wide) { Tag = tag; Reference = reference; Wide = wide; }
    }

    private static class Kernels
    {
        public static Packed MakePacked(byte tag, short signed, ulong wide, float number, byte tail) => new(tag, signed, wide, number, tail);
        public static (uint, int, ulong, uint, uint) ObservePacked(byte tag, short signed, ulong wide, float number, byte tail)
        {
            Packed value = MakePacked(tag, signed, wide, number, tail);
            return (value.Tag, value.Signed, value.Wide, BitConverter.SingleToUInt32Bits(value.Number), value.Tail);
        }
        public static Packed MutatePacked(uint low)
        {
            Packed value = MakePacked(0xAA, 0, 0, 0, 0x55);
            SetSigned(ref value.Signed); SetWide(ref value.Wide, low); SetNumber(ref value.Number);
            return value;
        }
        private static void SetSigned(ref short field) => field = unchecked((short)0xEEFF);
        private static void SetWide(ref ulong field, uint low) => field = 0x0123456700000000ul | low;
        private static void SetNumber(ref float field) => field = BitConverter.UInt32BitsToSingle(0xDEADBEEF);
        public static uint RecursiveOwners(uint count)
        {
            uint local = count;
            if (count != 0) { AddChild(ref local, count - 1); }
            return local;
        }
        private static void AddChild(ref uint local, uint count) => local += RecursiveOwners(count);
        public static WithReference MakeReference(object? reference, ulong wide) => new(0x5A, reference, wide);
        public static BoolPair MakeBoolean(bool flag, byte tail) => new(flag, tail);
        public static ulong InstanceAndGeneric(uint low)
        {
            Packed value = MakePacked(0xAA, 0, 0x0123456700000000ul | low, 0, 0x55);
            Swap(ref value.Tag, ref value.Tail);
            return value.Project();
        }
        private static void Swap<T>(ref T left, ref T right) { T copy = left; left = right; right = copy; }
        public static WithReference InstallReference(object? reference)
        {
            WithReference value = default; SetReference(ref value.Reference, reference); return value;
        }
        private static void SetReference(ref object? field, object? reference) => field = reference;
        public static uint ExternalByref(ref uint value) => value;
        public static object HeapConstruction() => new();
        public static int Checked(int value) => checked(value + 1);
        public static int Divide(int value, int divisor) => value / divisor;
        public static uint WithInitializer() => Initializer.Value;
    }
    private static class Initializer { public static uint Value = 7; }
}
