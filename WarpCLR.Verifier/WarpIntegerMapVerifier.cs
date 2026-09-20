using System.Reflection;
using WarpCLR.IR;

namespace WarpCLR.Verifier;

internal sealed class WarpIntegerMapVerifier
{
    public WarpIntegerMapKernel Verify(WarpIntegerMapRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        MethodInfo entry = request.Method;
        ValidateMethod(entry, request.InputBufferCount, isEntry: true);

        var builder = new ReflectionMethodGraphBuilder(entry);
        builder.Discover();

        WarpIntegerMapMethodBody entryBody = CreateBody(
            entry,
            GetEntryIdentity(entry),
            request.InputBufferCount,
            builder.GetCallTargets(entry));
        WarpIntegerMapMethodBody[] functions = builder.Functions
            .Select(
                method => CreateBody(
                    method,
                    GetIdentity(method),
                    inputBufferCount: 0,
                    callTargets: builder.GetCallTargets(method)))
            .ToArray();

        return WarpIntegerMapCilVerifier.Verify(entryBody, functions);
    }

    private static WarpIntegerMapMethodBody CreateBody(
        MethodInfo method,
        string identity,
        int inputBufferCount,
        IReadOnlyDictionary<int, WarpCilCallTarget> callTargets)
    {
        MethodBody body = method.GetMethodBody()
            ?? throw SignatureError(method, "The method does not have a CIL body.");

        if (body.ExceptionHandlingClauses.Count != 0)
        {
            throw SignatureError(method, "Exception regions are outside the integer map profile.");
        }

        foreach (LocalVariableInfo local in body.LocalVariables)
        {
            if (local.LocalType != typeof(uint) &&
                local.LocalType != typeof(bool))
            {
                throw SignatureError(
                    method,
                    "All local variables must have type System.UInt32 or System.Boolean.");
            }
        }

        byte[] il = body.GetILAsByteArray()
            ?? throw SignatureError(method, "The method does not contain CIL bytes.");

        return new WarpIntegerMapMethodBody(
            identity,
            method.GetParameters().Length,
            inputBufferCount,
            body.MaxStackSize,
            body.LocalVariables.Count,
            il,
            localsInitialized: body.InitLocals,
            callTargets: callTargets);
    }

    private static void ValidateMethod(
        MethodInfo method,
        int inputBufferCount,
        bool isEntry)
    {
        if (!method.IsStatic)
        {
            throw SignatureError(method, "The method must be static.");
        }

        if (method.IsAbstract || method.ContainsGenericParameters || method.IsGenericMethodDefinition)
        {
            throw SignatureError(method, "The method must be concrete and nongeneric.");
        }

        if (method.CallingConvention != CallingConventions.Standard)
        {
            throw SignatureError(method, "The method must use the default managed calling convention.");
        }

        if (method.DeclaringType is null ||
            method.DeclaringType.DeclaringType is not null ||
            method.DeclaringType.IsGenericType)
        {
            throw SignatureError(method, "The declaring type must be top-level and nongeneric.");
        }

        if (method.ReturnType != typeof(uint))
        {
            throw SignatureError(method, "The method return type must be System.UInt32.");
        }

        ParameterInfo[] parameters = method.GetParameters();
        if (isEntry && parameters.Length < inputBufferCount)
        {
            throw SignatureError(method, "The entry point does not declare all input buffer values.");
        }

        foreach (ParameterInfo parameter in parameters)
        {
            if (parameter.ParameterType != typeof(uint))
            {
                throw SignatureError(method, "All method parameters must have type System.UInt32.");
            }
        }
    }

    private static string GetIdentity(MethodInfo method) =>
        $"{method.DeclaringType?.FullName ?? "<global>"}.{method.Name}/{method.GetParameters().Length}";

    private static string GetEntryIdentity(MethodInfo method) =>
        $"{method.DeclaringType?.FullName ?? "<global>"}.{method.Name}";

    private static WarpVerificationException SignatureError(MethodInfo method, string message)
    {
        string identity = GetIdentity(method);
        return new WarpVerificationException(
            "WRPCIL1000",
            $"Method '{identity}' is invalid. {message}");
    }

    private sealed class ReflectionMethodGraphBuilder
    {
        private readonly MethodInfo entry;
        private readonly Dictionary<MethodInfo, int> functionIds = new();
        private readonly Dictionary<MethodInfo, IReadOnlyDictionary<int, WarpCilCallTarget>> calls = new();
        private readonly HashSet<MethodInfo> visiting = [];
        private readonly HashSet<MethodInfo> visited = [];
        private readonly List<MethodInfo> functions = [];

        public ReflectionMethodGraphBuilder(MethodInfo entry)
        {
            this.entry = entry;
        }

        public IReadOnlyList<MethodInfo> Functions => functions;

        public void Discover() => Visit(entry);

        public IReadOnlyDictionary<int, WarpCilCallTarget> GetCallTargets(MethodInfo method) =>
            calls.TryGetValue(method, out IReadOnlyDictionary<int, WarpCilCallTarget>? result)
                ? result
                : new Dictionary<int, WarpCilCallTarget>();

        private void Visit(MethodInfo method)
        {
            if (visited.Contains(method))
            {
                return;
            }

            if (!visiting.Add(method))
            {
                throw new WarpVerificationException(
                    "WRPCIL1014",
                    $"Method '{GetIdentity(method)}' is in a recursive call graph. " +
                    "Recursion requires the portable logical stack.");
            }

            MethodBody body = method.GetMethodBody()
                ?? throw SignatureError(method, "The method does not have a CIL body.");
            byte[] il = body.GetILAsByteArray()
                ?? throw SignatureError(method, "The method does not contain CIL bytes.");
            var methodCalls = new Dictionary<int, WarpCilCallTarget>();

            foreach (int token in WarpIntegerMapCilVerifier.ReadCallTokens(il).Distinct())
            {
                MethodBase? resolved;
                try
                {
                    resolved = method.Module.ResolveMethod(token);
                }
                catch (ArgumentException exception)
                {
                    throw new WarpVerificationException(
                        "WRPCIL1013",
                        $"Method '{GetIdentity(method)}' contains an unresolved call token " +
                        $"0x{token:X8}. {exception.Message}");
                }

                if (resolved is not MethodInfo target || target.Module != entry.Module)
                {
                    throw new WarpVerificationException(
                        "WRPCIL1013",
                        $"Method '{GetIdentity(method)}' calls outside its closed module.");
                }

                ValidateMethod(target, inputBufferCount: 0, isEntry: false);
                if (visiting.Contains(target))
                {
                    throw new WarpVerificationException(
                        "WRPCIL1014",
                        $"Call from '{GetIdentity(method)}' to '{GetIdentity(target)}' is recursive. " +
                        "Recursion requires the portable logical stack.");
                }

                if (!functionIds.TryGetValue(target, out int functionId))
                {
                    functionId = functions.Count;
                    functionIds.Add(target, functionId);
                    functions.Add(target);
                }

                methodCalls.Add(
                    token,
                    new WarpCilCallTarget(
                        functionId,
                        target.GetParameters().Length,
                        GetIdentity(target)));
                Visit(target);
            }

            calls.Add(method, methodCalls);
            visiting.Remove(method);
            visited.Add(method);
        }
    }
}
