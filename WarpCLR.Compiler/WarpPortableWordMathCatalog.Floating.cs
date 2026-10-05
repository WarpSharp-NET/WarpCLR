using System.Reflection;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableWordMathCatalog
{
    private static WarpPortableWordMathBinding? Floating(MethodInfo source, bool single)
    {
        string name = source.Name;
        int words = single ? 1 : 2;
        if (name is "Abs" or "Min" or "Max")
        {
            return Bind(source, typeof(WarpPortableNumericComparisons), WarpPortableNumericComparisons.Semantics, (single ? "Binary32" : "Binary64") + name);
        }
        if (name is "Sqrt" or "IEEERemainder" or "FusedMultiplyAdd")
        {
            return Bind(source, single ? typeof(WarpPortableBinary32Math) : typeof(WarpPortableBinary64Math),
                single ? WarpPortableBinary32Math.Semantics : WarpPortableBinary64Math.Semantics, name is "IEEERemainder" ? "IeeeRemainder" : name);
        }
        Type integrals = single ? typeof(WarpPortableBinary32Intrinsics) : typeof(WarpPortableBinary64Intrinsics);
        string integralSemantics = single ? WarpPortableBinary32Intrinsics.Semantics : WarpPortableBinary64Intrinsics.Semantics;
        if (name is "Floor" or "Ceiling" or "Truncate" or "CopySign" or "BitIncrement" or "BitDecrement" or "ILogB" or "ScaleB")
        {
            return Bind(source, integrals, integralSemantics, name);
        }
        if (name is "Sign")
        {
            return Bind(source, integrals, integralSemantics, name, fault: "SignFault", faultWords: single ? [0] : [0, 1],
                faults: [new(1, typeof(ArithmeticException).FullName!)]);
        }
        if (name is "Round")
        {
            ParameterInfo[] parameters = source.GetParameters();
            if (parameters.Length == 1) { return Bind(source, integrals, integralSemantics, "RoundToEven"); }
            bool digits = parameters[1].ParameterType == typeof(int);
            return Bind(source, integrals, integralSemantics, digits ? "RoundDigits" : "Round",
                extra: digits && parameters.Length == 2 ? [0] : [], fault: digits ? "RoundDigitsFault" : "RoundFault",
                faultWords: digits ? [words, words + 1] : [words], faults: digits ?
                    [new(2, typeof(ArgumentOutOfRangeException).FullName!), new(3, typeof(ArgumentException).FullName!)] : [new(3, typeof(ArgumentException).FullName!)]);
        }
        if (name is "Clamp")
        {
            string prefix = single ? "Binary32" : "Binary64";
            return Bind(source, typeof(WarpPortableNumericClamp), WarpPortableNumericClamp.Semantics, prefix,
                fault: prefix + "Fault", faultWords: single ? [1, 2] : [2, 3, 4, 5], faults: [new(3, typeof(ArgumentException).FullName!)]);
        }
        if (name is "Sin" or "Cos" or "Tan" or "Exp" or "Log" or "Log2" or "Log10" or "Atan" or "Asin" or "Acos" or
            "Sinh" or "Cosh" or "Tanh" or "Asinh" or "Acosh" or "Atanh" or "Cbrt" or "Atan2" or "Pow")
        {
            return Bind(source, single ? typeof(WarpPortableBinary32Transcendentals) : typeof(WarpPortableBinary64Transcendentals),
                single ? WarpPortableBinary32Transcendentals.Semantics : WarpPortableBinary64Transcendentals.Semantics,
                name is "Log" && source.GetParameters().Length == 2 ? "LogBase" : name);
        }
        return null;
    }
}
