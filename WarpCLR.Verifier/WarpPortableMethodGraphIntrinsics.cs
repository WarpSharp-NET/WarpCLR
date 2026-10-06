using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace WarpCLR.Verifier;

internal static partial class WarpPortableMethodGraphIntrinsics
{
    public const string MathContract = "warp.math.binary32.arithmetic/rne-gradual-canonical-nan/0.1|warp.math.binary64.arithmetic/rne-gradual-canonical-nan/0.1|" + NumericSignatureContract;

    public static bool IsLeafType(Type type) =>
        type.IsPrimitive || type.IsArray || type == typeof(void) || type == typeof(object) || type == typeof(string) ||
        type == typeof(Array) || type == typeof(ValueType) || type == typeof(Enum) || type == typeof(Type) || type == typeof(MemberInfo) ||
        type == typeof(RuntimeTypeHandle) || type == typeof(RuntimeFieldHandle) || type == typeof(RuntimeMethodHandle) ||
        type == typeof(Delegate) || type == typeof(MulticastDelegate) || IsDelegate(type) ||
        type == typeof(Math) || type == typeof(MathF) || type == typeof(BitConverter) || type == typeof(BitOperations) ||
        type == typeof(Interlocked) || type == typeof(Volatile) || type == typeof(Monitor) ||
        type == typeof(Thread) || type == typeof(RuntimeHelpers) || type == typeof(Activator) ||
        type.Assembly == typeof(object).Assembly && (type.IsEnum || type.IsInterface || typeof(Exception).IsAssignableFrom(type));

    public static bool IsStructuralTuple(Type type) => type.Assembly == typeof(object).Assembly && type.IsValueType &&
        (type == typeof(ValueTuple) || type.IsGenericType && type.GetGenericTypeDefinition().FullName?.StartsWith("System.ValueTuple`", StringComparison.Ordinal) == true);

    public static bool IsStructuralNullable(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);

    public static bool IsDelegate(Type type) => typeof(MulticastDelegate).IsAssignableFrom(type) && type != typeof(MulticastDelegate);

    public static string? Resolve(MethodBase method)
    {
        Type? type = method.DeclaringType;
        if (type is null)
        {
            return null;
        }

        ParameterInfo[] parameters = method.GetParameters();
        string? operation = ResolveObjectOperation(method, type, parameters) ?? ResolveNullableOperation(method, type, parameters) ?? ResolveNumericOperation(method, type, parameters) ??
            ResolveBitOperationsOperation(method) ?? ResolveMemoryOperation(method, type, parameters) ?? ResolveDataOperation(method, type, parameters) ??
            ResolveStringOperation(method, type, parameters) ?? ResolveManagedOperation(method, type, parameters);
        if (operation is null)
        {
            return null;
        }

        string signature = string.Join(",", parameters.Select(parameter =>
            parameter.ParameterType == typeof(IntPtr) && string.Equals(operation, "delegate.bind", StringComparison.Ordinal)
                ? "verified-method-target" : parameter.ParameterType.FullName));
        string returnType = method is MethodInfo function ? function.ReturnType.FullName! : "System.Void";
        return "warp.portable-intrinsic/" + operation + "/" + type.FullName + "(" + signature + ")->" + returnType + "/0.1";
    }

    private static string? ResolveObjectOperation(MethodBase method, Type type, ParameterInfo[] parameters)
    {
        if (method is ConstructorInfo && IsStructuralTuple(type) && parameters.Select(parameter => parameter.ParameterType).SequenceEqual(type.GetGenericArguments()))
        {
            return "value-tuple.construct";
        }
        else if (method is ConstructorInfo && type == typeof(object) && parameters.Length == 0)
        {
            return "object.base-constructor";
        }
        else if (method is ConstructorInfo && type.Assembly == typeof(object).Assembly && typeof(Exception).IsAssignableFrom(type) &&
            parameters.All(parameter => parameter.ParameterType == typeof(string) || typeof(Exception).IsAssignableFrom(parameter.ParameterType)))
        {
            return "exception.construct";
        }
        else if (method is ConstructorInfo && IsDelegate(type) && parameters.Length == 2 &&
            parameters[0].ParameterType == typeof(object) && parameters[1].ParameterType == typeof(IntPtr))
        {
            // The native-looking CLR constructor parameter is consumed only by
            // verified ldftn/ldvirtftn closure binding, never a raw portable value.
            return "delegate.bind";
        }
        else if (IsDelegate(type) && string.Equals(method.Name, "Invoke", StringComparison.Ordinal))
        {
            return "delegate.invoke";
        }
        else if (type.IsArray && method.Name is ".ctor" or "Get" or "Set" or "Address")
        {
            return "array." + method.Name;
        }
        else if (type == typeof(object) && parameters.Length == 0 && string.Equals(method.Name, nameof(GetType), StringComparison.Ordinal))
        {
            return "object.type-of";
        }
        else if (type == typeof(object) && !method.IsStatic && method is MethodInfo { ReturnType: var hashResult } &&
            hashResult == typeof(int) && parameters.Length == 0 && method.Name is nameof(GetHashCode))
        {
            return "object.reference-hash";
        }
        else if (type == typeof(RuntimeHelpers) && method.IsStatic && method is MethodInfo { ReturnType: var identityResult } &&
            identityResult == typeof(int) && parameters.Length == 1 && parameters[0].ParameterType == typeof(object) && method.Name is nameof(RuntimeHelpers.GetHashCode))
        {
            return "object.reference-hash-null-zero";
        }
        else if (type == typeof(object) && method.IsStatic && parameters.Length == 2 &&
            parameters.All(parameter => parameter.ParameterType == typeof(object)) &&
            string.Equals(method.Name, nameof(ReferenceEquals), StringComparison.Ordinal))
        {
            return "object.reference-equality";
        }
        else if (type == typeof(Type) && method.IsStatic && parameters.Length == 1 && parameters[0].ParameterType == typeof(RuntimeTypeHandle) &&
            string.Equals(method.Name, nameof(Type.GetTypeFromHandle), StringComparison.Ordinal))
        {
            return "type.from-handle";
        }

        return null;
    }

    private static string? ResolveNumericOperation(MethodBase method, Type type, ParameterInfo[] parameters)
    {
        if ((type == typeof(Math) || type == typeof(MathF)) && method is MethodInfo { IsStatic: true, IsGenericMethod: false } numeric &&
            IsExactMathSignature(numeric, parameters))
        {
            return "math.strict." + method.Name;
        }
        else if (type == typeof(BitConverter) && method.IsStatic && parameters.Length == 1 &&
            method.Name is nameof(BitConverter.SingleToUInt32Bits) or nameof(BitConverter.UInt32BitsToSingle) or
                nameof(BitConverter.DoubleToUInt64Bits) or nameof(BitConverter.UInt64BitsToDouble) or
                nameof(BitConverter.SingleToInt32Bits) or nameof(BitConverter.Int32BitsToSingle) or
                nameof(BitConverter.DoubleToInt64Bits) or nameof(BitConverter.Int64BitsToDouble))
        {
            return "numeric.bit-cast." + method.Name;
        }

        return null;
    }

    private static string? ResolveMemoryOperation(MethodBase method, Type type, ParameterInfo[] parameters)
    {
        if (type == typeof(Interlocked) && method.IsStatic && parameters.Length is >= 1 and <= 3 &&
            parameters[0].ParameterType.IsByRef && method.Name is nameof(Interlocked.Increment) or nameof(Interlocked.Decrement) or
                nameof(Interlocked.Add) or nameof(Interlocked.Exchange) or nameof(Interlocked.CompareExchange) or
                nameof(Interlocked.And) or nameof(Interlocked.Or) or nameof(Interlocked.Read))
        {
            return "memory.atomic.sc." + method.Name;
        }
        else if (type == typeof(Volatile) && method.IsStatic && parameters.Length is 1 or 2 &&
            parameters[0].ParameterType.IsByRef && method.Name is nameof(Volatile.Read) or nameof(Volatile.Write))
        {
            return "memory.volatile.acquire-release." + method.Name;
        }
        else if (type == typeof(Thread) && method.IsStatic && parameters.Length == 0 &&
            string.Equals(method.Name, nameof(Thread.MemoryBarrier), StringComparison.Ordinal))
        {
            return "memory.fence.sc";
        }
        else if (type == typeof(Monitor) && method.IsStatic && parameters.Length is 1 or 2 &&
            parameters[0].ParameterType == typeof(object) && (parameters.Length == 1 || parameters[1].ParameterType == typeof(bool).MakeByRefType()) &&
            method.Name is nameof(Monitor.Enter) or nameof(Monitor.Exit))
        {
            return "memory.monitor." + method.Name;
        }

        return null;
    }

    private static string? ResolveDataOperation(MethodBase method, Type type, ParameterInfo[] parameters)
    {
        if (type == typeof(RuntimeHelpers) && method.IsStatic && parameters.Length == 2 &&
            parameters[0].ParameterType == typeof(Array) && parameters[1].ParameterType == typeof(RuntimeFieldHandle) &&
            string.Equals(method.Name, nameof(RuntimeHelpers.InitializeArray), StringComparison.Ordinal))
        {
            return "array.initialize-data";
        }

        return null;
    }

}
