using System.Numerics;
using System.Runtime.InteropServices;

namespace WarpCLR.Tests.Production;

internal static partial class WarpPortableCollectiveOracle
{
    private static long arithmeticWitnesses;
    internal static long ArithmeticWitnesses => Interlocked.Read(ref arithmeticWitnesses);

    internal static Outcome[] Evaluate(uint type, uint operation, uint overflow, uint kind, ulong[] inputs)
    {
        int width = 1;
        while (width < inputs.Length) { width *= 2; }
        int count = kind == 1 ? 1 : inputs.Length;
        var output = new Outcome[count];
        for (int i = 0; i < count; i++)
        {
            int end = kind == 1 ? inputs.Length : kind == 2 ? i + 1 : i;
            output[i] = Prefix(inputs, type, operation, overflow, 0, width, end);
        }
        return output;
    }

    private static Outcome Prefix(ulong[] inputs, uint type, uint operation, uint overflow, int start, int width, int end)
    {
        if (start >= end || start >= inputs.Length) { return new(Identity(type, operation), false); }
        if (width == 1) { return new(inputs[start], true); }
        int half = width / 2;
        Outcome left = Prefix(inputs, type, operation, overflow, start, half, end);
        Outcome right = Prefix(inputs, type, operation, overflow, start + half, half, end);
        if (!left.Present) { return right; }
        if (!right.Present) { return left; }
        if (left.FaultStart >= 0) { return left; }
        if (right.FaultStart >= 0) { return right; }
        return Combine(type, operation, overflow, left.Bits, right.Bits, start, Math.Min(end, start + width));
    }

    private static Outcome Combine(uint type, uint operation, uint overflow, ulong left, ulong right, int start, int end)
    {
        Interlocked.Increment(ref arithmeticWitnesses);
        if (type >= 5) { return new(FloatCombine(type, operation, left, right), true); }
        int width = type <= 2 ? 32 : 64;
        bool signed = type == 2 || type == 4;
        BigInteger modulus = BigInteger.One << width;
        BigInteger a = Integer(left, modulus, signed), b = Integer(right, modulus, signed);
        BigInteger value = operation == 1 ? a + b : operation == 4 ? a * b :
            operation == 2 ? BigInteger.Min(a, b) : BigInteger.Max(a, b);
        BigInteger minimum = signed ? -(modulus >> 1) : BigInteger.Zero;
        BigInteger maximum = signed ? (modulus >> 1) - 1 : modulus - 1;
        if (overflow != 0 && (value < minimum || value > maximum)) { return new(0, true, start, end); }
        return new((ulong)(value & (modulus - 1)), true);
    }

    private static BigInteger Integer(ulong value, BigInteger modulus, bool signed) =>
        signed && value >= (modulus >> 1) ? (BigInteger)value - modulus : value;

    private static ulong Identity(uint type, uint operation)
    {
        if (operation == 1) { return 0; }
        if (operation == 4) { return type == 5 ? 0x3F800000UL : type == 6 ? 0x3FF0000000000000UL : 1; }
        if (type >= 5)
        {
            return type == 5 ? operation == 2 ? 0x7F800000UL : 0xFF800000UL :
                operation == 2 ? 0x7FF0000000000000UL : 0xFFF0000000000000UL;
        }
        if (type == 1) { return operation == 2 ? uint.MaxValue : 0; }
        if (type == 2) { return operation == 2 ? int.MaxValue : 0x80000000UL; }
        if (type == 3) { return operation == 2 ? ulong.MaxValue : 0; }
        return operation == 2 ? long.MaxValue : 0x8000000000000000UL;
    }

    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct Outcome(ulong Bits, bool Present, int FaultStart = -1, int FaultEnd = -1);
}
