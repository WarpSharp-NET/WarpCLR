using System.Reflection;

namespace WarpCLR.Verifier;

internal static partial class WarpPortableMethodGraphIntrinsics
{
    private static string? ResolveStringOperation(MethodBase method, Type type, ParameterInfo[] parameters)
    {
        if (type != typeof(string))
        {
            return null;
        }

        if (method is ConstructorInfo && (parameters.Length == 1 && parameters[0].ParameterType == typeof(char[]) ||
            parameters.Length == 2 && parameters[0].ParameterType == typeof(char) && parameters[1].ParameterType == typeof(int) ||
            parameters.Length == 3 && parameters[0].ParameterType == typeof(char[]) && parameters[1].ParameterType == typeof(int) && parameters[2].ParameterType == typeof(int)))
        {
            return "string.construct";
        }

        if (!method.IsStatic && parameters.Length == 0 && method.Name is "get_Length" or nameof(ToString))
        {
            return "string." + method.Name;
        }

        if (!method.IsStatic && parameters.Length == 1 && parameters[0].ParameterType == typeof(int) && method.Name is "get_Chars")
        {
            return "string.character";
        }

        if (!method.IsStatic && parameters.Length is 1 or 2 && parameters.All(parameter => parameter.ParameterType == typeof(int)) &&
            method.Name is nameof(string.Substring))
        {
            return "string.substring";
        }

        if (method.IsStatic && parameters.Length is >= 1 and <= 4 &&
            parameters.All(parameter => parameter.ParameterType == typeof(string) || parameter.ParameterType == typeof(string[])) && method.Name is nameof(string.Concat))
        {
            return "string.concat";
        }

        if (parameters.Length is 1 or 2 && parameters.All(parameter => parameter.ParameterType == typeof(string)) && method.Name is nameof(string.Equals))
        {
            return "string.ordinal-equality";
        }

        if (method.IsStatic && parameters.Length == 2 && parameters.All(parameter => parameter.ParameterType == typeof(string)) && method.Name is nameof(string.CompareOrdinal))
        {
            return "string.ordinal-compare";
        }

        return null;
    }

    private static string? ResolveManagedOperation(MethodBase method, Type type, ParameterInfo[] parameters)
    {
        if (type == typeof(Activator) && method is MethodInfo { IsStatic: true, IsGenericMethod: true, ContainsGenericParameters: false } &&
            parameters.Length == 0 && method.Name is nameof(Activator.CreateInstance))
        {
            return "object.default-construct";
        }

        if (type == typeof(Array) && method is MethodInfo { IsStatic: true, IsGenericMethod: true, ContainsGenericParameters: false } &&
            parameters.Length == 0 && method.Name is nameof(Array.Empty))
        {
            return "array.empty";
        }

        if (type.Assembly == typeof(object).Assembly && typeof(Exception).IsAssignableFrom(type) &&
            !method.IsStatic && parameters.Length == 0 && method.Name is "get_Message" or "get_InnerException" or "get_StackTrace" or nameof(Exception.GetBaseException))
        {
            return "exception." + method.Name;
        }

        if (type == typeof(Type) && method.IsStatic && parameters.Length == 2 &&
            parameters.All(parameter => parameter.ParameterType == typeof(Type)) && method.Name is "op_Equality" or "op_Inequality")
        {
            return "type.identity." + method.Name;
        }

        if (type == typeof(Delegate) && method.IsStatic && parameters.Length is 1 or 2 &&
            parameters.All(parameter => parameter.ParameterType == typeof(Delegate) || parameter.ParameterType == typeof(Delegate[])) &&
            method.Name is nameof(Delegate.Combine) or nameof(Delegate.Remove) or nameof(Delegate.RemoveAll))
        {
            return "delegate." + method.Name;
        }

        return null;
    }
}
