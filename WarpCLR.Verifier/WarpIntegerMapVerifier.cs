using System.Collections.Immutable;
using System.Reflection;
using WarpCLR.IR;

namespace WarpCLR.Verifier;

internal sealed class WarpIntegerMapVerifier
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Preserve the verifier service instance dependency used by the pipeline constructor and friend test assemblies.")]
    public WarpIntegerMapKernel Verify(WarpIntegerMapRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        MethodInfo entry = request.Method;
        WarpInitializationAdmission.RequireModule(entry.Module);
        ValidateMethod(entry, request.InputBufferCount, isEntry: true, request.WordArena, multipleBanks: request.WordBanks is not null);

        var builder = new ReflectionMethodGraphBuilder(entry, request.WordArena, request.WordBanks);
        builder.Discover();

        WarpIntegerMapMethodBody entryBody = CreateBody(
            entry,
            GetEntryIdentity(entry),
            request.InputBufferCount,
            builder.GetCallTargets(entry), request.WordArena, request.WordBanks);
        WarpIntegerMapMethodBody[] functions = builder.Functions
            .Select(
                method => CreateBody(
                    method,
                    GetIdentity(method),
                    inputBufferCount: 0,
                    callTargets: builder.GetCallTargets(method), request.WordArena, request.WordBanks))
            .ToArray();

        return WarpIntegerMapCilVerifier.Verify(entryBody, functions);
    }

    private static WarpIntegerMapMethodBody CreateBody(
        MethodInfo method,
        string identity,
        int inputBufferCount,
        IReadOnlyDictionary<int, WarpCilCallTarget> callTargets,
        bool wordArena = false, WarpWordBankBindings? wordBanks = null)
    {
        MethodBody body = method.GetMethodBody()
            ?? throw SignatureError(method, "The method does not have a CIL body.");

        if (body.ExceptionHandlingClauses.Count != 0)
        {
            throw SignatureError(method, "Exception regions are outside the integer map profile.");
        }

        WarpCompilationAdmission.Require(identity, WarpCompilationResourceKind.Locals,
            body.LocalVariables.Count, WarpCompilationAdmission.MaximumLocalsPerBody);
        var localTypes = ImmutableArray.CreateBuilder<WarpMetadataType>(body.LocalVariables.Count);
        foreach (LocalVariableInfo local in body.LocalVariables)
        {
            if (local.LocalType != typeof(uint) &&
                local.LocalType != typeof(bool))
            {
                throw SignatureError(
                    method,
                    "All local variables must have type System.UInt32 or System.Boolean.");
            }

            localTypes.Add(local.LocalType == typeof(bool) ? WarpMetadataType.Boolean : WarpMetadataType.UInt32);
        }

        byte[] il = body.GetILAsByteArray()
            ?? throw SignatureError(method, "The method does not contain CIL bytes.");

        return new WarpIntegerMapMethodBody(
            identity,
            method.GetParameters().Length,
            inputBufferCount,
            body.MaxStackSize,
            localTypes.MoveToImmutable(),
            il,
            localsInitialized: body.InitLocals,
            callTargets: callTargets,
            wordArena: wordArena,
            arenaParameters: method.GetParameters().Select(parameter => parameter.ParameterType == typeof(uint[])).ToImmutableArray(),
            arenaElementTokens: wordArena ? ResolveArenaElements(method, il) : null,
            stateParameters: wordBanks?.GetStateParameters(method) ?? default);
    }

    private static ImmutableHashSet<int> ResolveArenaElements(MethodInfo method, byte[] il)
    {
        var tokens = ImmutableHashSet.CreateBuilder<int>();
        foreach ((int token, int offset) in WarpIntegerMapCilVerifier.ReadArenaElementTokens(il, GetIdentity(method)))
        {
            Type element;
            try
            {
                element = method.Module.ResolveType(token);
            }
            catch (ArgumentException exception)
            {
                throw new WarpVerificationException("WRPCIL1017", "An arena element token is invalid: " + exception.Message, offset);
            }

            if (element != typeof(uint))
            {
                throw new WarpVerificationException("WRPCIL1017", "An arena address must point to an exact System.UInt32 element.", offset);
            }

            tokens.Add(token);
        }

        return tokens.ToImmutable();
    }

    private static void ValidateMethod(
        MethodInfo method,
        int inputBufferCount,
        bool isEntry,
        bool wordArena = false, bool multipleBanks = false)
    {
        WarpInitializationAdmission.RequireMethod(method);
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
            if (parameter.ParameterType != typeof(uint) && (!wordArena || parameter.ParameterType != typeof(uint[])))
            {
                throw SignatureError(method, "All method parameters must have type System.UInt32.");
            }
        }

        if (!multipleBanks && parameters.Count(parameter => parameter.ParameterType == typeof(uint[])) > 1)
        {
            throw SignatureError(method, "A portable word service can bind only one context arena.");
        }
    }

    private static string GetIdentity(MethodInfo method)
    {
        string entryIdentity = GetEntryIdentity(method);
        string identity = $"{entryIdentity}/{method.GetParameters().Length}";
        WarpCompilationAdmission.Require("<reflection-method>", WarpCompilationResourceKind.IdentityCharacters,
            identity.Length, WarpCompilationAdmission.MaximumIdentityCharacters);
        return identity;
    }

    private static string GetEntryIdentity(MethodInfo method)
    {
        string type = method.DeclaringType?.FullName ?? "<global>";
        WarpCompilationAdmission.Require("<reflection-method>", WarpCompilationResourceKind.IdentityCharacters,
            type.Length + (long)method.Name.Length + 1, WarpCompilationAdmission.MaximumIdentityCharacters);
        return $"{type}.{method.Name}";
    }

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
        private readonly bool wordArena;
        private readonly WarpWordBankBindings? wordBanks;
        private readonly Dictionary<MethodInfo, int> functionIds = new();
        private readonly Dictionary<MethodInfo, IReadOnlyDictionary<int, WarpCilCallTarget>> calls = new();
        private readonly HashSet<MethodInfo> visiting = [];
        private readonly HashSet<MethodInfo> visited = [];
        private readonly List<MethodInfo> functions = [];
        private readonly WarpCilCompilationAdmission admission;

        public ReflectionMethodGraphBuilder(MethodInfo entry, bool wordArena, WarpWordBankBindings? wordBanks)
        {
            this.entry = entry;
            this.wordArena = wordArena;
            this.wordBanks = wordBanks;
            admission = new WarpCilCompilationAdmission(GetEntryIdentity(entry));
        }

        public IReadOnlyList<MethodInfo> Functions => functions;

        public void Discover() => Visit(entry);

        public IReadOnlyDictionary<int, WarpCilCallTarget> GetCallTargets(MethodInfo method) =>
            calls.TryGetValue(method, out IReadOnlyDictionary<int, WarpCilCallTarget>? result)
                ? result
                : new Dictionary<int, WarpCilCallTarget>();

        private MethodInfo ResolveCallTarget(MethodInfo method, int token)
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

            return target;
        }

        private WarpCilCallTarget CreateCallTarget(MethodInfo target, int functionId) =>
            new(functionId, target.GetParameters().Length, GetIdentity(target))
            {
                ArenaParameters = target.GetParameters().Select(parameter => parameter.ParameterType == typeof(uint[])).ToImmutableArray(),
                StateParameters = wordBanks?.GetStateParameters(target) ?? default,
            };

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
            admission.AdmitMethod(GetIdentity(method), method.GetParameters().Length, body.MaxStackSize,
                body.LocalVariables.Count, il.Length, isEntry: method == entry);
            var methodCalls = new Dictionary<int, WarpCilCallTarget>();

            IReadOnlyList<int> callTokens = WarpIntegerMapCilVerifier.ReadCallTokens(il, GetIdentity(method), out int instructionCount);
            admission.AdmitDecodedInstructions(instructionCount);
            foreach (int token in callTokens.Distinct())
            {
                MethodInfo target = ResolveCallTarget(method, token);
                ValidateMethod(target, inputBufferCount: 0, isEntry: false, wordArena, multipleBanks: wordBanks is not null);
                if (visiting.Contains(target))
                {
                    throw new WarpVerificationException(
                        "WRPCIL1014",
                        $"Call from '{GetIdentity(method)}' to '{GetIdentity(target)}' is recursive. " +
                        "Recursion requires the portable logical stack.");
                }

                if (!functionIds.TryGetValue(target, out int functionId))
                {
                    WarpCompilationAdmission.Require(GetEntryIdentity(entry), WarpCompilationResourceKind.Functions,
                        functions.Count + 1L, WarpCompilationAdmission.MaximumFunctionsPerEntry);
                    functionId = functions.Count;
                    functionIds.Add(target, functionId);
                    functions.Add(target);
                }

                methodCalls.Add(token, CreateCallTarget(target, functionId));
                Visit(target);
            }

            calls.Add(method, methodCalls);
            visiting.Remove(method);
            visited.Add(method);
        }
    }
}
