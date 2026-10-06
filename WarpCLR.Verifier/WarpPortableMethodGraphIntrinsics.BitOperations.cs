using System.Numerics;
using System.Reflection;

namespace WarpCLR.Verifier;

internal static partial class WarpPortableMethodGraphIntrinsics
{
    internal const string RotateLeftContract = "warp.bit-operations/rotate-left-u32-u64-raw-int32-count-mod-width-uint-words/0.1";

    private static readonly MethodInfo RotateLeftUInt32 = RequiredRotateLeft(typeof(uint));
    private static readonly MethodInfo RotateLeftUInt64 = RequiredRotateLeft(typeof(ulong));

    internal static int RotateLeftWordWidth(MethodBase method)
    {
        if (method is not MethodInfo { IsStatic: true, IsGenericMethod: false } function ||
            function.ContainsGenericParameters || function.DeclaringType != typeof(BitOperations) ||
            function.Module.Assembly != typeof(object).Assembly)
        {
            return 0;
        }

        return RotateLeftUInt32.Equals(function) ? 32 : RotateLeftUInt64.Equals(function) ? 64 : 0;
    }

    private static MethodInfo RequiredRotateLeft(Type valueType)
    {
        MethodInfo method = typeof(BitOperations).GetMethod(nameof(BitOperations.RotateLeft),
            BindingFlags.Public | BindingFlags.Static, [valueType, typeof(int)]) ??
            throw new InvalidOperationException("The exact corelib unsigned RotateLeft overload is unavailable.");
        ParameterInfo[] parameters = method.GetParameters();
        if (method.ReturnType != valueType || method.IsGenericMethod || method.ContainsGenericParameters ||
            method.DeclaringType != typeof(BitOperations) || method.Module.Assembly != typeof(object).Assembly ||
            parameters.Length != 2 || parameters[0].ParameterType != valueType || parameters[1].ParameterType != typeof(int))
        {
            throw new InvalidOperationException("RotateLeft requires its exact resolved corelib method identity.");
        }
        return method;
    }

    private static string? ResolveBitOperationsOperation(MethodBase method) => RotateLeftWordWidth(method) switch
    {
        32 => "numeric.bit-operations.rotate-left.uint32/" + RotateLeftContract,
        64 => "numeric.bit-operations.rotate-left.uint64/" + RotateLeftContract,
        _ => null,
    };
}
