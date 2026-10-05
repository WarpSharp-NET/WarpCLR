using System.Reflection;

namespace WarpCLR.Verifier;

internal static partial class WarpPortableMethodGraphIntrinsics
{
    internal const string NumericSignatureContract = "warp.math.overloads/exact-signed-width-enum-fault/0.1";

    private static bool IsExactMathSignature(MethodInfo method, ParameterInfo[] parameters)
    {
        if (parameters.Length == 0) { return false; }
        Type input = parameters[0].ParameterType;
        Type output = method.ReturnType;
        bool floating = input == typeof(float) || input == typeof(double);
        bool same = parameters.All(parameter => parameter.ParameterType == input);
        if (floating)
        {
            if (same && output == input && parameters.Length == 1 && IsFloatingUnary(method.Name)) { return true; }
            if (same && output == input && parameters.Length == 2 && method.Name is
                nameof(Math.Atan2) or nameof(Math.CopySign) or nameof(Math.IEEERemainder) or nameof(Math.Log) or
                nameof(Math.Max) or nameof(Math.Min) or nameof(Math.Pow)) { return true; }
            if (same && output == input && parameters.Length == 3 && method.Name is nameof(Math.Clamp) or nameof(Math.FusedMultiplyAdd)) { return true; }
            if (parameters.Length == 1 && output == typeof(int) && method.Name is nameof(Math.Sign) or nameof(Math.ILogB)) { return true; }
            if (output == input && parameters.Length == 2 && parameters[1].ParameterType == typeof(int) && method.Name is nameof(Math.ScaleB)) { return true; }
            if (output == input && method.Name is nameof(Math.Round))
            {
                return parameters.Length == 2 && (parameters[1].ParameterType == typeof(int) || parameters[1].ParameterType == typeof(MidpointRounding)) ||
                    parameters.Length == 3 && parameters[1].ParameterType == typeof(int) && parameters[2].ParameterType == typeof(MidpointRounding);
            }
            return false;
        }
        bool signed = input == typeof(sbyte) || input == typeof(short) || input == typeof(int) || input == typeof(long);
        bool integer = signed || input == typeof(byte) || input == typeof(ushort) || input == typeof(uint) || input == typeof(ulong);
        if (!integer || !same) { return false; }
        return parameters.Length == 1 && signed && method.Name is nameof(Math.Abs) && output == input ||
            parameters.Length == 1 && signed && method.Name is nameof(Math.Sign) && output == typeof(int) ||
            parameters.Length == 2 && output == input && method.Name is nameof(Math.Min) or nameof(Math.Max) ||
            parameters.Length == 3 && output == input && method.Name is nameof(Math.Clamp);
    }

    private static bool IsFloatingUnary(string name) => name is nameof(Math.Abs) or nameof(Math.Acos) or nameof(Math.Acosh) or
        nameof(Math.Asin) or nameof(Math.Asinh) or nameof(Math.Atan) or nameof(Math.Atanh) or nameof(Math.BitDecrement) or nameof(Math.BitIncrement) or
        nameof(Math.Cbrt) or nameof(Math.Ceiling) or nameof(Math.Cos) or nameof(Math.Cosh) or nameof(Math.Exp) or nameof(Math.Floor) or
        nameof(Math.Log) or nameof(Math.Log2) or nameof(Math.Log10) or nameof(Math.Round) or nameof(Math.Sin) or nameof(Math.Sinh) or
        nameof(Math.Sqrt) or nameof(Math.Tan) or nameof(Math.Tanh) or nameof(Math.Truncate);
}
