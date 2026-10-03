using System.Buffers;
using System.Text.Json;

namespace WarpCLR.IR;

public sealed record WarpArtifactSidecar(
    string Profile,
    string DeviceAbi,
    string Entry,
    WarpBackendKind Backend,
    WarpArtifactFormat Format,
    WarpConformanceStatus Conformance,
    string ManifestHash,
    string AssemblyHash,
    string GraphHash,
    string IrHash,
    string ModuleHash,
    string ModulePath);
