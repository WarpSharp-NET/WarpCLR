using System.Collections.ObjectModel;
using WarpCLR.IR;

namespace WarpCLR.Compiler;

public sealed class WarpCompilation
{
    internal WarpCompilation(
        WarpLinearKernel kernel,
        IDictionary<WarpBackendKind, WarpBackendArtifact> artifacts)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentNullException.ThrowIfNull(artifacts);

        Kernel = kernel;
        Artifacts = new ReadOnlyDictionary<WarpBackendKind, WarpBackendArtifact>(
            new Dictionary<WarpBackendKind, WarpBackendArtifact>(artifacts));
    }

    public WarpLinearKernel Kernel { get; }

    public IReadOnlyDictionary<WarpBackendKind, WarpBackendArtifact> Artifacts { get; }
}
