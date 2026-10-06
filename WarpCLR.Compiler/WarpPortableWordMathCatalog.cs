using System.Collections.Immutable;
using System.Reflection;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableWordMathCatalog
{
    internal static WarpPortableWordMathBinding? Resolve(MethodInfo source)
    {
        int rotateWidth = WarpPortableMethodGraphIntrinsics.RotateLeftWordWidth(source);
        if (rotateWidth != 0)
        {
            return Bind(source, typeof(WarpPortableBitOperations), WarpPortableBitOperations.Semantics,
                rotateWidth == 32 ? nameof(WarpPortableBitOperations.RotateLeft32) : "RotateLeft64");
        }
        string? intrinsic = WarpPortableMethodGraphIntrinsics.Resolve(source);
        if (intrinsic?.Contains("math.strict.", StringComparison.Ordinal) != true) { return null; }
        Type input = source.GetParameters()[0].ParameterType;
        return input == typeof(float) || input == typeof(double) ? Floating(source, input == typeof(float)) : Integer(source, input);
    }

    private static WarpPortableWordMathBinding Bind(MethodInfo source, Type implementation, string semantics, string operation,
        uint[]? extra = null, string? fault = null, int[]? faultWords = null, WarpPortableWordMathFault[]? faults = null)
    {
        bool wideResult = source.ReturnType == typeof(double) || source.ReturnType == typeof(long) || source.ReturnType == typeof(ulong);
        string signature = source.DeclaringType!.FullName + "." + source.Name + "(" + string.Join(',', source.GetParameters().Select(parameter => parameter.ParameterType.FullName)) + ")->" + source.ReturnType.FullName;
        return new(signature, implementation, semantics, wideResult ? [operation + "Low", operation + "High"] : [operation],
            extra?.ToImmutableArray() ?? [], fault, faultWords?.ToImmutableArray() ?? [], faults?.ToImmutableArray() ?? []);
    }
}
