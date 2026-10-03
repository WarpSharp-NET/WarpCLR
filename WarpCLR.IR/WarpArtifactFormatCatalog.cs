using System.Security.Cryptography;
using System.Text;

namespace WarpCLR.IR;

public static class WarpArtifactFormatCatalog
{
    public static WarpArtifactFormat ForBackend(WarpBackendKind backend) => backend switch
    {
        WarpBackendKind.CoreCLR => WarpArtifactFormat.CoreCLRPlan,
        WarpBackendKind.NVPTX => WarpArtifactFormat.NVPTX,
        WarpBackendKind.AMDGPU => WarpArtifactFormat.AMDGPULLVMIR,
        WarpBackendKind.SPIRV => WarpArtifactFormat.SPIRVLLVMIR,
        _ => throw new ArgumentOutOfRangeException(
            nameof(backend),
            backend,
            "The backend is not registered."),
    };
}
