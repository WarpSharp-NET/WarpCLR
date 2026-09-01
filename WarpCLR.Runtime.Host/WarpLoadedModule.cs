using System.Collections.ObjectModel;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

public sealed class WarpLoadedEntry
{
    private readonly ReadOnlyDictionary<WarpBackendKind, WarpLoadedArtifact> artifacts;

    internal WarpLoadedEntry(
        string identity,
        WarpLinearKernel kernel,
        IDictionary<WarpBackendKind, WarpLoadedArtifact> artifacts)
    {
        Identity = identity;
        Kernel = kernel;
        this.artifacts = new ReadOnlyDictionary<WarpBackendKind, WarpLoadedArtifact>(
            new Dictionary<WarpBackendKind, WarpLoadedArtifact>(artifacts));
    }

    public string Identity { get; }

    public int InputBufferCount => Kernel.InputBufferCount;

    public int ScalarArgumentCount => Kernel.ScalarArgumentCount;

    internal WarpLinearKernel Kernel { get; }

    internal IReadOnlyDictionary<WarpBackendKind, WarpLoadedArtifact> Artifacts => artifacts;
}

internal sealed class WarpLoadedArtifact
{
    public WarpLoadedArtifact(WarpArtifactSidecar sidecar, ReadOnlySpan<byte> content)
    {
        Sidecar = sidecar;
        Content = content.ToArray();
    }

    public WarpArtifactSidecar Sidecar { get; }

    public byte[] Content { get; }
}

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
