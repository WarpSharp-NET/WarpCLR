using System.Reflection;
using WarpCLR.IR;

namespace WarpCLR.Verifier;

internal sealed class WarpIntegerMapVerifier
{
    public WarpIntegerMapKernel Verify(WarpIntegerMapRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        MethodInfo method = request.Method;
        ValidateMethod(method, request.InputBufferCount);

        MethodBody body = method.GetMethodBody()
            ?? throw SignatureError(method, "The entry point does not have a CIL body.");

        if (body.ExceptionHandlingClauses.Count != 0)
        {
            throw SignatureError(method, "Exception regions are outside the integer map profile.");
        }

        foreach (LocalVariableInfo local in body.LocalVariables)
        {
            if (local.LocalType != typeof(uint))
            {
                throw SignatureError(method, "All local variables must have type System.UInt32.");
            }
        }

        byte[] il = body.GetILAsByteArray()
            ?? throw SignatureError(method, "The entry point does not contain CIL bytes.");
        string identity = $"{method.DeclaringType?.FullName ?? "<global>"}.{method.Name}";

        return WarpIntegerMapCilVerifier.Verify(
            new WarpIntegerMapMethodBody(
                identity,
                method.GetParameters().Length,
                request.InputBufferCount,
                body.MaxStackSize,
                body.LocalVariables.Count,
                il));
    }

    private static void ValidateMethod(MethodInfo method, int inputBufferCount)
    {
        if (!method.IsStatic)
        {
            throw SignatureError(method, "The entry point must be static.");
        }

        if (method.IsAbstract || method.ContainsGenericParameters || method.IsGenericMethodDefinition)
        {
            throw SignatureError(method, "The entry point must be concrete and nongeneric.");
        }

        if (method.ReturnType != typeof(uint))
        {
            throw SignatureError(method, "The entry point return type must be System.UInt32.");
        }

        ParameterInfo[] parameters = method.GetParameters();
        if (parameters.Length < inputBufferCount)
        {
            throw SignatureError(method, "The entry point does not declare all input buffer values.");
        }

        foreach (ParameterInfo parameter in parameters)
        {
            if (parameter.ParameterType != typeof(uint))
            {
                throw SignatureError(method, "All entry point parameters must have type System.UInt32.");
            }
        }
    }

    private static WarpVerificationException SignatureError(MethodInfo method, string message)
    {
        string identity = $"{method.DeclaringType?.FullName ?? "<global>"}.{method.Name}";
        return new WarpVerificationException("WRPCIL1000", $"Entry point '{identity}' is invalid. {message}");
    }
}
