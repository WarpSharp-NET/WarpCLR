using System.Reflection;
using System.Runtime.CompilerServices;

namespace WarpCLR.Verifier;

internal static partial class WarpPortableMethodGraphIntrinsics
{
    public const string MathContract = "warp.math.binary32.arithmetic/rne-gradual-canonical-nan/0.1|warp.math.binary64.arithmetic/rne-gradual-canonical-nan/0.1";

    public static bool IsLeafType(Type type) =>
        type.IsPrimitive || type.IsArray || type == typeof(void) || type == typeof(object) || type == typeof(string) ||
        type == typeof(Array) || type == typeof(ValueType) || type == typeof(Enum) || type == typeof(Type) ||
        type == typeof(RuntimeTypeHandle) || type == typeof(RuntimeFieldHandle) || type == typeof(RuntimeMethodHandle) ||
        type == typeof(Delegate) || type == typeof(MulticastDelegate) || IsDelegate(type) ||
        type == typeof(Math) || type == typeof(MathF) || type == typeof(BitConverter) ||
        type == typeof(Interlocked) || type == typeof(Volatile) || type == typeof(Monitor) ||
        type == typeof(Thread) || type == typeof(RuntimeHelpers) || type == typeof(Activator) ||
        type.Assembly == typeof(object).Assembly && (type.IsEnum || type.IsInterface || typeof(Exception).IsAssignableFrom(type));

    public static bool IsDelegate(Type type) => typeof(MulticastDelegate).IsAssignableFrom(type) && type != typeof(MulticastDelegate);

    public static string? Resolve(MethodBase method)
    {
        Type? type = method.DeclaringType;
        if (type is null)
        {
            return null;
        }

        ParameterInfo[] parameters = method.GetParameters();
        string? operation = ResolveObjectOperation(method, type, parameters) ?? ResolveNumericOperation(method, type, parameters) ??
            ResolveMemoryOperation(method, type, parameters) ?? ResolveDataOperation(method, type, parameters) ??
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
        if (method is ConstructorInfo && type == typeof(object) && parameters.Length == 0)
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
            IsMathOperation(numeric.Name) && parameters.All(parameter => parameter.ParameterType == typeof(float) ||
                parameter.ParameterType == typeof(double) || parameter.ParameterType == typeof(int)) &&
            (numeric.ReturnType == typeof(float) || numeric.ReturnType == typeof(double) || numeric.ReturnType == typeof(int)))
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

    private static bool IsMathOperation(string name) => name is
        nameof(Math.Abs) or nameof(Math.Acos) or nameof(Math.Acosh) or nameof(Math.Asin) or nameof(Math.Asinh) or
        nameof(Math.Atan) or nameof(Math.Atan2) or nameof(Math.Atanh) or nameof(Math.BitDecrement) or nameof(Math.BitIncrement) or
        nameof(Math.Cbrt) or nameof(Math.Ceiling) or nameof(Math.Clamp) or nameof(Math.CopySign) or nameof(Math.Cos) or
        nameof(Math.Cosh) or nameof(Math.Exp) or nameof(Math.Floor) or nameof(Math.FusedMultiplyAdd) or nameof(Math.IEEERemainder) or
        nameof(Math.ILogB) or nameof(Math.Log) or nameof(Math.Log2) or nameof(Math.Log10) or nameof(Math.Max) or nameof(Math.Min) or
        nameof(Math.Pow) or nameof(Math.Round) or nameof(Math.ScaleB) or nameof(Math.Sign) or nameof(Math.Sin) or nameof(Math.Sinh) or
        nameof(Math.Sqrt) or nameof(Math.Tan) or nameof(Math.Tanh) or nameof(Math.Truncate);
}
