using System.Buffers;
using WarpCLR.IR;

namespace WarpCLR.Verifier;

internal sealed record WarpManifestEntryData(
    string Type,
    string Method,
    WarpReductionOperation? Reduction,
    IReadOnlyList<WarpParameterRole> ParameterRoles,
    IReadOnlyList<string> Capabilities,
    string GraphHash);
