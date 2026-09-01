using System.Collections.ObjectModel;
using WarpCLR.IR;

namespace WarpCLR.Verifier;

public enum WarpParameterRole
{
    InputBuffer,
    Scalar,
}

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

public sealed class WarpVerifiedModule
{
    internal WarpVerifiedModule(
        string manifestHash,
        string assemblyHash,
        string producer,
        string producerVersion,
        IEnumerable<WarpVerifiedEntry> entries)
    {
        ManifestHash = manifestHash;
        AssemblyHash = assemblyHash;
        Producer = producer;
        ProducerVersion = producerVersion;
        Entries = Array.AsReadOnly(entries.ToArray());
    }

    public string ManifestHash { get; }

    public string AssemblyHash { get; }

    public string Producer { get; }

    public string ProducerVersion { get; }

    public ReadOnlyCollection<WarpVerifiedEntry> Entries { get; }
}
