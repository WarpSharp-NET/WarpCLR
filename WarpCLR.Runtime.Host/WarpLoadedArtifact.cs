using System.Collections.ObjectModel;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

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
