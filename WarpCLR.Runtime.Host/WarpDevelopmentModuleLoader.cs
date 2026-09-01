using System.Security.Cryptography;
using WarpCLR.Backend.Cpu;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Runtime.Host;

public sealed class WarpDevelopmentModuleLoader
{
    private readonly WarpModuleVerifier verifier = new();
    private readonly WarpIntegerMapLowerer lowerer = new();

    public WarpLoadedModule Load(
        string assemblyPath,
        string packageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        return Load(File.ReadAllBytes(assemblyPath), packageDirectory);
    }

    public WarpLoadedModule Load(
        ReadOnlyMemory<byte> assemblyBytes,
        string packageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        if (!Directory.Exists(packageDirectory))
        {
            throw Error("WRPHOST1000", "The AOT package directory does not exist.");
        }

        WarpVerifiedModule module = verifier.Verify(assemblyBytes);
        Dictionary<(string Entry, WarpBackendKind Backend), WarpLoadedArtifact> loadedArtifacts =
            ReadArtifacts(packageDirectory, module);
        var loadedEntries = new Dictionary<string, WarpLoadedEntry>(StringComparer.Ordinal);

        foreach (WarpVerifiedEntry entry in module.Entries)
        {
            WarpLinearKernel expectedKernel = lowerer.Lower(entry.Kernel);
            string expectedIrHash = WarpIrHash.Compute(expectedKernel);
            var entryArtifacts = new Dictionary<WarpBackendKind, WarpLoadedArtifact>();

            foreach (WarpBackendKind backend in WarpBackendCatalog.Required)
            {
                if (!loadedArtifacts.TryGetValue((entry.Identity, backend), out WarpLoadedArtifact? artifact))
                {
                    throw Error(
                        "WRPHOST1000",
                        $"The AOT package is missing {backend} for entry '{entry.Identity}'.");
                }

                if (!string.Equals(artifact.Sidecar.IrHash, expectedIrHash, StringComparison.Ordinal))
                {
                    throw Error(
                        "WRPHOST1000",
                        $"The {backend} IR hash does not match entry '{entry.Identity}'.");
                }

                entryArtifacts.Add(backend, artifact);
            }

            WarpLoadedArtifact cpuArtifact = entryArtifacts[WarpBackendKind.CpuReference];
            byte[] expectedCpuPlan = WarpCpuPlanCodec.Serialize(expectedKernel);
            if (!cpuArtifact.Content.AsSpan().SequenceEqual(expectedCpuPlan))
            {
                throw Error(
                    "WRPHOST1000",
                    $"The CPU plan does not match entry '{entry.Identity}'.");
            }

            _ = WarpCpuPlanCodec.Deserialize(
                cpuArtifact.Content,
                entry.Identity,
                expectedKernel.InputBufferCount,
                expectedKernel.ScalarArgumentCount);
            loadedEntries.Add(
                entry.Identity,
                new WarpLoadedEntry(entry.Identity, expectedKernel, entryArtifacts));
        }

        int expectedArtifactCount = module.Entries.Count * WarpBackendCatalog.Required.Count;
        if (loadedArtifacts.Count != expectedArtifactCount)
        {
            throw Error("WRPHOST1000", "The AOT package contains an unexpected artifact sidecar.");
        }

        return new WarpLoadedModule(module.ManifestHash, module.AssemblyHash, loadedEntries);
    }

    private static Dictionary<(string Entry, WarpBackendKind Backend), WarpLoadedArtifact> ReadArtifacts(
        string packageDirectory,
        WarpVerifiedModule module)
    {
        var verifiedEntries = module.Entries.ToDictionary(entry => entry.Identity, StringComparer.Ordinal);
        var artifacts = new Dictionary<(string Entry, WarpBackendKind Backend), WarpLoadedArtifact>();

        foreach (string sidecarPath in Directory
                     .EnumerateFiles(packageDirectory, "*.warp.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            WarpArtifactSidecar sidecar;
            try
            {
                sidecar = WarpArtifactSidecarCodec.Deserialize(File.ReadAllBytes(sidecarPath));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException)
            {
                throw Error("WRPHOST1000", "An AOT package sidecar is invalid.", exception);
            }

            if (!verifiedEntries.TryGetValue(sidecar.Entry, out WarpVerifiedEntry? entry))
            {
                throw Error("WRPHOST1000", $"Package entry '{sidecar.Entry}' is not in the verified manifest.");
            }

            ValidateSidecar(sidecar, module, entry);
            string modulePath = Path.Combine(packageDirectory, sidecar.ModulePath);
            if (!File.Exists(modulePath))
            {
                throw Error("WRPHOST1000", $"Package module '{sidecar.ModulePath}' does not exist.");
            }

            byte[] content = File.ReadAllBytes(modulePath);
            string contentHash = Convert.ToHexString(SHA256.HashData(content));
            if (!string.Equals(contentHash, sidecar.ModuleHash, StringComparison.Ordinal))
            {
                throw Error("WRPHOST1000", $"Package module '{sidecar.ModulePath}' has a changed hash.");
            }

            var key = (sidecar.Entry, sidecar.Backend);
            if (!artifacts.TryAdd(key, new WarpLoadedArtifact(sidecar, content)))
            {
                throw Error(
                    "WRPHOST1000",
                    $"Package entry '{sidecar.Entry}' has duplicate {sidecar.Backend} sidecars.");
            }
        }

        return artifacts;
    }

    private static void ValidateSidecar(
        WarpArtifactSidecar sidecar,
        WarpVerifiedModule module,
        WarpVerifiedEntry entry)
    {
        if (!string.Equals(sidecar.ManifestHash, module.ManifestHash, StringComparison.Ordinal) ||
            !string.Equals(sidecar.AssemblyHash, module.AssemblyHash, StringComparison.Ordinal) ||
            !string.Equals(sidecar.GraphHash, entry.GraphHash, StringComparison.Ordinal))
        {
            throw Error("WRPHOST1000", $"Package sidecar for '{entry.Identity}' has stale identity data.");
        }
    }

    private static WarpHostException Error(string code, string message) => new(code, message);

    private static WarpHostException Error(
        string code,
        string message,
        Exception innerException) => new(code, message, innerException);
}
