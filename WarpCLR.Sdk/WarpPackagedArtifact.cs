using System.Collections.ObjectModel;
using System.Security.Cryptography;
using WarpCLR.IR;

namespace WarpCLR.Sdk;

public sealed record WarpPackagedArtifact(
    string ModulePath,
    string SidecarPath,
    WarpArtifactSidecar Sidecar);
