using System.Collections.ObjectModel;

namespace WarpCLR.IR;

public static class WarpBackendCatalog
{
    private static readonly ReadOnlyCollection<WarpBackendKind> RequiredBackends =
        Array.AsReadOnly(
        [
            WarpBackendKind.CoreCLR,
            WarpBackendKind.NVPTX,
            WarpBackendKind.AMDGPU,
            WarpBackendKind.SPIRV,
        ]);

    public static IReadOnlyList<WarpBackendKind> Required => RequiredBackends;
}
