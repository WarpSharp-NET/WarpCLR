using System.Collections.Immutable;
using System.Reflection;
using WarpCLR.IR;

namespace WarpCLR.Verifier;

internal sealed class WarpWordBankBindings
{
    private readonly ImmutableDictionary<MethodInfo, ImmutableArray<WarpRuntimeWordBank>> methods;

    internal WarpWordBankBindings(IReadOnlyDictionary<MethodInfo, IReadOnlyList<WarpRuntimeWordBank>> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        WarpCompilationAdmission.Require("<runtime-word-bank-bindings>", WarpCompilationResourceKind.Functions,
            bindings.Count, WarpCompilationAdmission.MaximumFunctionsPerEntry + 1L);
        var copy = ImmutableDictionary.CreateBuilder<MethodInfo, ImmutableArray<WarpRuntimeWordBank>>();
        foreach ((MethodInfo method, IReadOnlyList<WarpRuntimeWordBank> parameters) in bindings)
        {
            ArgumentNullException.ThrowIfNull(method, nameof(bindings));
            ArgumentNullException.ThrowIfNull(parameters, nameof(bindings));
            ImmutableArray<WarpRuntimeWordBank> owned = parameters.ToImmutableArray();
            Validate(method, owned);
            copy.Add(method, owned);
        }
        methods = copy.ToImmutable();
        string[] identities = methods.Select(pair => pair.Key.Module.ModuleVersionId.ToString("D") + "/" +
            pair.Key.MetadataToken.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) + "/" +
            string.Join(',', pair.Value.Select(bank => bank.ToString()))).Order(StringComparer.Ordinal).ToArray();
        Identity = WarpPortableSnapshotIdentity.Hash(new { Banks = identities });
    }

    internal string Identity { get; }

    internal ImmutableArray<bool> GetStateParameters(MethodInfo method)
    {
        if (methods.TryGetValue(method, out ImmutableArray<WarpRuntimeWordBank> parameters))
        {
            return parameters.Select(bank => bank == WarpRuntimeWordBank.State).ToImmutableArray();
        }
        ParameterInfo[] signature = method.GetParameters();
        if (signature.Any(parameter => parameter.ParameterType == typeof(uint[])))
        {
            throw new ArgumentException("Every word bank in the discovered helper closure requires an explicit binding.", nameof(method));
        }
        return ImmutableArray.CreateRange(Enumerable.Repeat(false, signature.Length));
    }

    private static void Validate(MethodInfo method, ImmutableArray<WarpRuntimeWordBank> bindings)
    {
        ParameterInfo[] parameters = method.GetParameters();
        WarpCompilationAdmission.Require(method.Name, WarpCompilationResourceKind.Parameters,
            parameters.Length, WarpCompilationAdmission.MaximumParametersPerBody);
        if (parameters.Length != bindings.Length)
        {
            throw new ArgumentException("Word-bank bindings must match the exact method signature.", nameof(bindings));
        }
        for (int index = 0; index < parameters.Length; index++)
        {
            WarpRuntimeWordBank binding = bindings[index];
            if (!Enum.IsDefined(binding) || (parameters[index].ParameterType == typeof(uint[])
                ? binding == WarpRuntimeWordBank.Word : binding != WarpRuntimeWordBank.Word))
            {
                throw new ArgumentException("Only exact array parameters can bind a runtime word bank.", nameof(bindings));
            }
        }
    }
}
