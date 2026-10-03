using System.Collections.ObjectModel;
using WarpCLR.IR;

namespace WarpCLR.Verifier;

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
