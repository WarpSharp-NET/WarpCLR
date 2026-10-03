using System.Buffers;
using System.Text;
using System.Text.Json;
using WarpCLR.IR;

namespace WarpCLR.Verifier;

internal sealed record WarpManifestEntryData(
    string Type,
    string Method,
    WarpReductionOperation? Reduction,
    IReadOnlyList<WarpParameterRole> ParameterRoles,
    IReadOnlyList<string> Capabilities,
    string GraphHash);
