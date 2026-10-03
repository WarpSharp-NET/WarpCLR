using System.Collections.ObjectModel;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

public sealed class WarpLoadedModule
{
    private readonly ReadOnlyDictionary<string, WarpLoadedEntry> entries;

    internal WarpLoadedModule(
        string manifestHash,
        string assemblyHash,
        IDictionary<string, WarpLoadedEntry> entries)
    {
        ManifestHash = manifestHash;
        AssemblyHash = assemblyHash;
        this.entries = new ReadOnlyDictionary<string, WarpLoadedEntry>(
            new Dictionary<string, WarpLoadedEntry>(entries, StringComparer.Ordinal));
    }

    public string ManifestHash { get; }

    public string AssemblyHash { get; }

    public IReadOnlyDictionary<string, WarpLoadedEntry> Entries => entries;
}
