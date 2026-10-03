using System.Collections.ObjectModel;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

public sealed class WarpLoadedEntry
{
    private readonly ReadOnlyDictionary<WarpBackendKind, WarpLoadedArtifact> artifacts;

    internal WarpLoadedEntry(
        string identity,
        WarpControlFlowKernel kernel,
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

    internal WarpControlFlowKernel Kernel { get; }

    internal IReadOnlyDictionary<WarpBackendKind, WarpLoadedArtifact> Artifacts => artifacts;
}
