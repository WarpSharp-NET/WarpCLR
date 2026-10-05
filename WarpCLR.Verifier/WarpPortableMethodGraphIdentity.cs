using System.Globalization;
using System.Reflection;
using WarpCLR.IR;

namespace WarpCLR.Verifier;

internal static class WarpPortableMethodGraphIdentity
{
    public static string Type(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        ValidateTypeShape(type);
        string identity;
        if (type.HasElementType)
        {
            identity = Type(type.GetElementType()!) + (type.IsByRef ? "&" : type.IsSZArray ? "[]" :
                type.GetArrayRank() == 1 ? "[*]" : "[" + new string(',', type.GetArrayRank() - 1) + "]");
        }
        else
        {
            string name = type.IsGenericType ? type.GetGenericTypeDefinition().FullName! : type.FullName ?? type.Name;
            identity = "[" + type.Assembly.FullName + "/" + type.Module.ModuleVersionId.ToString("D", CultureInfo.InvariantCulture) + "]" + name;
            if (type.IsGenericType)
            {
                identity += "<" + string.Join(",", type.GetGenericArguments().Select(Type)) + ">";
            }
        }
        WarpCompilationAdmission.Require("<portable-type>", WarpCompilationResourceKind.IdentityCharacters,
            identity.Length, WarpCompilationAdmission.MaximumIdentityCharacters);
        return identity;
    }

    public static string Method(MethodBase method)
    {
        string declaringType = Type(method.DeclaringType ?? throw Error("A global method is not portable."));
        string arguments = method is MethodInfo { IsGenericMethod: true } info
            ? "<" + string.Join(",", info.GetGenericArguments().Select(Type)) + ">" : string.Empty;
        bool delegateConstructor = method is ConstructorInfo && WarpPortableMethodGraphIntrinsics.IsDelegate(method.DeclaringType!);
        string parameters = string.Join(",", method.GetParameters().Select(parameter =>
            delegateConstructor && parameter.ParameterType == typeof(IntPtr) ? "verified-method-target" : Type(parameter.ParameterType)));
        string result = method is MethodInfo function ? Type(function.ReturnType) : Type(typeof(void));
        string identity = declaringType + "::" + method.Name + "#" + method.MetadataToken.ToString("X8", CultureInfo.InvariantCulture) +
            arguments + "(" + parameters + ")->" + result;
        WarpCompilationAdmission.Require("<portable-method>", WarpCompilationResourceKind.IdentityCharacters,
            identity.Length, WarpCompilationAdmission.MaximumIdentityCharacters);
        return identity;
    }

    public static string Field(FieldInfo field) =>
        Type(field.DeclaringType ?? throw Error("A global field is not portable.")) + "::" + field.Name + ":" + Type(field.FieldType);

    public static string Literal(object? value) => value switch
    {
        null => "null",
        float single => "binary32:" + BitConverter.SingleToUInt32Bits(single).ToString("X8", CultureInfo.InvariantCulture),
        double wide => "binary64:" + BitConverter.DoubleToUInt64Bits(wide).ToString("X16", CultureInfo.InvariantCulture),
        string text => "string:" + text,
        char character => "char:" + ((ushort)character).ToString("X4", CultureInfo.InvariantCulture),
        bool boolean => boolean ? "boolean:1" : "boolean:0",
        byte or sbyte or short or ushort or int or uint or long or ulong =>
            value.GetType().FullName + ":" + Convert.ToString(value, CultureInfo.InvariantCulture),
        _ => throw Error("A literal has an unsupported managed type."),
    };

    public static void ValidateTypeShape(Type type)
    {
        var pending = new Stack<(Type Type, int Depth)>();
        pending.Push((type, 0));
        while (pending.TryPop(out (Type Type, int Depth) item))
        {
            if (item.Depth > 32)
            {
                throw Error("A type exceeds the portable generic/element nesting limit of 32.");
            }

            if (item.Type.ContainsGenericParameters || item.Type.IsPointer || item.Type.IsFunctionPointer ||
                item.Type == typeof(TypedReference) || item.Type == typeof(ArgIterator) ||
                item.Type == typeof(IntPtr) || item.Type == typeof(UIntPtr) || item.Type.IsImport)
            {
                throw Error("Open types, native addresses, typed references and COM types are not portable.");
            }

            if (item.Type.HasElementType)
            {
                pending.Push((item.Type.GetElementType()!, item.Depth + 1));
            }

            if (item.Type.IsGenericType)
            {
                foreach (Type argument in item.Type.GetGenericArguments())
                {
                    pending.Push((argument, item.Depth + 1));
                }
            }
        }
    }

    private static WarpVerificationException Error(string message) => new("WRPCLR2100", message);
}
