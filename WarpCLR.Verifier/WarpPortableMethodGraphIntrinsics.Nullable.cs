using System.Reflection;

namespace WarpCLR.Verifier;

internal static partial class WarpPortableMethodGraphIntrinsics
{
    private static string? ResolveNullableOperation(MethodBase method, Type type, ParameterInfo[] parameters)
    {
        if (!IsStructuralNullable(type) || method.IsStatic) { return null; }
        Type element = type.GetGenericArguments()[0];
        if (method is ConstructorInfo && parameters.Length == 1 && parameters[0].ParameterType == element) { return "nullable.construct"; }
        if (method is not MethodInfo function) { return null; }
        if (method.Name is "get_HasValue" && parameters.Length == 0 && function.ReturnType == typeof(bool)) { return "nullable.has-value"; }
        if (method.Name is "get_Value" && parameters.Length == 0 && function.ReturnType == element) { return "nullable.value"; }
        if (method.Name is nameof(Nullable<int>.GetValueOrDefault) && function.ReturnType == element &&
            (parameters.Length == 0 || parameters.Length == 1 && parameters[0].ParameterType == element)) { return "nullable.value-or-default"; }
        return null;
    }
}
