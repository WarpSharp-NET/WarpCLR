using System.Collections.ObjectModel;

namespace WarpCLR.IR;

public enum WarpBackendKind
{
    CpuReference,
    Nvidia,
    Amd,
    Intel,
}

public static class WarpBackendCatalog
{
    private static readonly ReadOnlyCollection<WarpBackendKind> RequiredBackends =
        Array.AsReadOnly(
        [
            WarpBackendKind.CpuReference,
            WarpBackendKind.Nvidia,
            WarpBackendKind.Amd,
            WarpBackendKind.Intel,
        ]);

    public static IReadOnlyList<WarpBackendKind> Required => RequiredBackends;
}
