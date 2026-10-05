using System.Collections.Immutable;
using System.Reflection;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableSourceFaultServices
{
    internal const string Semantics = "warp.source-fault-ticket/privately-issued-operation-consistency-take-real-eh-publication-root-ack-no-grant/0.1";

    internal static ImmutableDictionary<MethodInfo, IReadOnlyList<WarpRuntimeWordBank>> BankBindings()
    {
        string[] mixed = [nameof(TakePreparedFault), nameof(RaiseTakenPreparedFault), nameof(AcknowledgeRaisedFault), nameof(ValidateTicket), nameof(ValidateTicketIdentity), nameof(ValidateTicketSite)];
        string[] arena = [nameof(Range), nameof(Prepared), nameof(Factory), nameof(Root), nameof(ExceptionRecord), nameof(Descriptor), nameof(MemoryDescriptor),
            nameof(HashMatches), nameof(Object), nameof(ValidRoot), nameof(RootMatches), nameof(NullRoot), nameof(ValidateRootPublication), nameof(ValidatePreparedData), nameof(ValidateResource)];
        var bindings = WarpPortableExceptionServices.BankBindings().ToBuilder();
        foreach (string name in mixed)
        {
            MethodInfo method = Method(name); ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length < 2 || parameters[0].ParameterType != typeof(uint[]) || parameters[1].ParameterType != typeof(uint[]) ||
                parameters.Skip(2).Any(parameter => parameter.ParameterType != typeof(uint)))
            { throw new InvalidOperationException("The explicit prepared-fault State/Arena signature has changed."); }
            bindings.Add(method, Array.AsReadOnly(parameters.Select((_, index) => index == 0 ? WarpRuntimeWordBank.State :
                index == 1 ? WarpRuntimeWordBank.Arena : WarpRuntimeWordBank.Word).ToArray()));
        }
        foreach (string name in arena)
        {
            MethodInfo method = Method(name); ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 0 || parameters[0].ParameterType != typeof(uint[]) || parameters.Skip(1).Any(parameter => parameter.ParameterType != typeof(uint)))
            { throw new InvalidOperationException("The explicit prepared-fault Arena signature has changed."); }
            bindings.Add(method, Array.AsReadOnly(parameters.Select((_, index) => index == 0 ? WarpRuntimeWordBank.Arena : WarpRuntimeWordBank.Word).ToArray()));
        }
        MethodInfo fits = Method(nameof(Fits));
        bindings.Add(fits, Array.AsReadOnly(fits.GetParameters().Select(_ => WarpRuntimeWordBank.Word).ToArray()));
        return bindings.ToImmutable();
    }

    internal static MethodInfo Method(string name) => typeof(WarpPortableSourceFaultServices).GetMethod(name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) ?? throw new ArgumentException("Unknown closed prepared-fault service.", nameof(name));
}
