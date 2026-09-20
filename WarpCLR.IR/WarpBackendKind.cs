using System.Collections.ObjectModel;

namespace WarpCLR.IR;

public enum WarpBackendKind
{
    CoreCLR,
    NVPTX,
    AMDGPU,
    SPIRV,
}

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
