using System.Reflection;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableWordMathCatalog
{
    private static WarpPortableWordMathBinding? Integer(MethodInfo source, Type input)
    {
        bool signed = input == typeof(sbyte) || input == typeof(short) || input == typeof(int) || input == typeof(long);
        bool wide = input == typeof(long) || input == typeof(ulong);
        bool integer = signed || input == typeof(byte) || input == typeof(ushort) || input == typeof(uint) || input == typeof(ulong);
        if (!integer) { return null; }
        string prefix = (signed ? "Signed" : "Unsigned") + (wide ? "64" : "32");
        uint width = input == typeof(sbyte) || input == typeof(byte) ? 8u : input == typeof(short) || input == typeof(ushort) ? 16u : wide ? 64u : 32u;
        string operation = prefix + source.Name;
        if (source.Name is "Abs")
        {
            return Bind(source, typeof(WarpPortableIntegerMath), WarpPortableIntegerMath.Semantics, operation,
                extra: wide ? [] : [width], fault: operation + "Fault", faultWords: [0, 1], faults: [new(2, typeof(OverflowException).FullName!)]);
        }
        if (source.Name is "Clamp")
        {
            return Bind(source, typeof(WarpPortableIntegerMath), WarpPortableIntegerMath.Semantics, operation,
                fault: operation + "Fault", faultWords: wide ? [2, 3, 4, 5] : [1, 2], faults: [new(3, typeof(ArgumentException).FullName!)]);
        }
        return source.Name is "Min" or "Max" or "Sign" ? Bind(source, typeof(WarpPortableIntegerMath), WarpPortableIntegerMath.Semantics, operation) : null;
    }
}
