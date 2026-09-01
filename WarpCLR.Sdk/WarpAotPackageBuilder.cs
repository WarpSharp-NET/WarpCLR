using System.Text;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Sdk;

public static class WarpAotPackageBuilder
{
    public static WarpAotPackage Build(WarpModuleCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var artifacts = new List<WarpPackagedArtifact>();
        var files = new SortedDictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        foreach (WarpVerifiedEntry entry in compilation.Module.Entries)
        {
            if (!compilation.Entries.TryGetValue(entry.Identity, out WarpCompilation? entryCompilation))
            {
                throw new InvalidOperationException($"Entry compilation '{entry.Identity}' is missing.");
            }

            string irHash = WarpIrHash.Compute(entryCompilation.Kernel);
            foreach (WarpBackendKind backend in WarpBackendCatalog.Required)
            {
                WarpBackendArtifact artifact = entryCompilation.Artifacts[backend];
                string modulePath = GetModulePath(entry, artifact);
                string sidecarPath = $"{modulePath}.warp.json";
                var sidecar = new WarpArtifactSidecar(
                    WarpProfileCatalog.ProfileId,
                    WarpDeviceAbi.Version,
                    entry.Identity,
                    backend,
                    artifact.Format,
                    artifact.ConformanceStatus,
                    compilation.Module.ManifestHash,
                    compilation.Module.AssemblyHash,
                    entry.GraphHash,
                    irHash,
                    artifact.ContentHash,
                    modulePath);

                byte[] sidecarBytes = WarpArtifactSidecarCodec.Serialize(sidecar);
                files.Add(modulePath, artifact.Content.ToArray());
                files.Add(sidecarPath, sidecarBytes);
                artifacts.Add(new WarpPackagedArtifact(modulePath, sidecarPath, sidecar));
            }
        }

        return new WarpAotPackage(artifacts, files);
    }

    private static string GetModulePath(
        WarpVerifiedEntry entry,
        WarpBackendArtifact artifact)
    {
        string identity = Sanitize(entry.Identity);
        string backend = WarpArtifactSidecarCodec.GetBackendName(artifact.Backend);
        string extension = artifact.Format switch
        {
            WarpArtifactFormat.CpuExpressionPlan => ".warpplan",
            WarpArtifactFormat.NvidiaPtx => ".ptx",
            WarpArtifactFormat.AmdLlvmIr => ".amdgpu.ll",
            WarpArtifactFormat.IntelSpirvLlvmIr => ".spirv.ll",
            _ => throw new ArgumentOutOfRangeException(
                nameof(artifact),
                artifact.Format,
                "The artifact format is not registered."),
        };

        return $"{identity}.{entry.GraphHash[..12]}.{backend}{extension}";
    }

    private static string Sanitize(string identity)
    {
        var result = new StringBuilder(Math.Min(identity.Length, 80));
        foreach (char character in identity)
        {
            if (result.Length == 80)
            {
                break;
            }

            bool letter = character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
            bool digit = character is >= '0' and <= '9';
            result.Append(letter || digit ? character : '_');
        }

        return result.Length == 0 ? "warp_entry" : result.ToString();
    }
}
