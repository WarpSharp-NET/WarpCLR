using System.Collections.ObjectModel;
using WarpCLR.IR;

namespace WarpCLR.Verifier;

public sealed class WarpVerifiedEntry
{
    internal WarpVerifiedEntry(
        string identity,
        string graphHash,
        IEnumerable<WarpParameterRole> parameterRoles,
        IEnumerable<string> requiredCapabilities,
        WarpIntegerMapKernel kernel)
    {
        Identity = identity;
        GraphHash = graphHash;
        ParameterRoles = Array.AsReadOnly(parameterRoles.ToArray());
        RequiredCapabilities = Array.AsReadOnly(requiredCapabilities.ToArray());
        Kernel = kernel;
    }

    public string Identity { get; }

    public string GraphHash { get; }

    public ReadOnlyCollection<WarpParameterRole> ParameterRoles { get; }

    public ReadOnlyCollection<string> RequiredCapabilities { get; }

    public WarpIntegerMapKernel Kernel { get; }
}
