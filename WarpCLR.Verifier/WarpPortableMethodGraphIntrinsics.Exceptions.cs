using System.Reflection;

namespace WarpCLR.Verifier;

internal static partial class WarpPortableMethodGraphIntrinsics
{
    internal const string ExceptionAccessorContract = "warp.exception-accessors/exact-corelib-module-token-declaring-type-signature-reflected-view-independent-closed-virtual-targets-base-data-distinct-from-formatted-message-and-trace/0.2";

    internal static WarpPortableExceptionAccessorKind? ExceptionAccessor(MethodBase method)
    {
        if (method is not MethodInfo { IsStatic: false, IsGenericMethod: false } function) { return null; }
        ParameterInfo[] parameters = function.GetParameters();
        if (function.DeclaringType == typeof(Exception) && function.ReturnType == typeof(void) && parameters.Length == 1 &&
            parameters[0].ParameterType == typeof(int) && SameExceptionMethod(function, typeof(Exception).GetProperty(nameof(Exception.HResult))!.SetMethod))
        {
            return WarpPortableExceptionAccessorKind.HResultStore;
        }
        if (parameters.Length != 0) { return null; }
        if (SameGetter(function, typeof(Exception), nameof(Exception.InnerException), typeof(Exception))) { return WarpPortableExceptionAccessorKind.InnerException; }
        if (SameGetter(function, typeof(Exception), nameof(Exception.HResult), typeof(int))) { return WarpPortableExceptionAccessorKind.HResult; }
        if (SameGetter(function, typeof(ArgumentException), nameof(ArgumentException.ParamName), typeof(string))) { return WarpPortableExceptionAccessorKind.ParamName; }
        if (SameGetter(function, typeof(ArgumentOutOfRangeException), nameof(ArgumentOutOfRangeException.ActualValue), typeof(object))) { return WarpPortableExceptionAccessorKind.ActualValue; }
        if (SameGetter(function, typeof(TypeInitializationException), nameof(TypeInitializationException.TypeName), typeof(string))) { return WarpPortableExceptionAccessorKind.TypeName; }
        if (SameExceptionMethod(function, typeof(Exception).GetMethod(nameof(Exception.GetBaseException), Type.EmptyTypes)) && function.ReturnType == typeof(Exception))
        {
            return WarpPortableExceptionAccessorKind.BaseException;
        }
        if (SameGetter(function, typeof(Exception), nameof(Exception.Message), typeof(string)) ||
            SameGetter(function, typeof(ArgumentException), nameof(Exception.Message), typeof(string)) ||
            SameGetter(function, typeof(OperationCanceledException), nameof(Exception.Message), typeof(string)) ||
            SameGetter(function, typeof(ArgumentOutOfRangeException), nameof(Exception.Message), typeof(string))) { return WarpPortableExceptionAccessorKind.Message; }
        if (SameGetter(function, typeof(Exception), nameof(Exception.StackTrace), typeof(string))) { return WarpPortableExceptionAccessorKind.StackTrace; }
        return null;
    }

    private static string? ResolveExceptionOperation(MethodBase method) => ExceptionAccessor(method) switch
    {
        WarpPortableExceptionAccessorKind.InnerException => "exception.data.v1.get_InnerException",
        WarpPortableExceptionAccessorKind.HResult => "exception.data.v1.get_HResult",
        WarpPortableExceptionAccessorKind.ParamName => "exception.data.v1.get_ParamName",
        WarpPortableExceptionAccessorKind.ActualValue => "exception.data.v1.get_ActualValue",
        WarpPortableExceptionAccessorKind.TypeName => "exception.data.v1.get_TypeName",
        WarpPortableExceptionAccessorKind.BaseException => "exception.data.v1.GetBaseException",
        WarpPortableExceptionAccessorKind.Message => "exception.formatted.v1.get_Message",
        WarpPortableExceptionAccessorKind.StackTrace => "exception.formatted.v1.get_StackTrace",
        WarpPortableExceptionAccessorKind.HResultStore => "exception.data.v1.set_HResult",
        _ => null,
    };

    private static bool SameGetter(MethodInfo function, Type declaring, string property, Type result) =>
        function.DeclaringType == declaring && function.ReturnType == result && SameExceptionMethod(function, declaring.GetProperty(property,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)?.GetMethod);

    internal static bool SameExceptionMethod(MethodInfo function, MethodInfo? expected) => expected is not null &&
        function.DeclaringType == expected.DeclaringType && function.Module == expected.Module && function.MetadataToken == expected.MetadataToken;
}
