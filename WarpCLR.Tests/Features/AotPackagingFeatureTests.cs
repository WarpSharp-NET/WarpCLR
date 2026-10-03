using WarpCLR.IR;
using WarpCLR.Sdk;

namespace WarpCLR.Tests.Features;

[TestClass]
[global::System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest creates this internal fixture through reflected discovery.")]
internal sealed class AotPackagingFeatureTests
{
    [TestMethod]
    [FourBackends]
    public void PackageBackendNamesMatchTargetIdentity(WarpBackendKind backend)
    {
        string expectedName = backend switch
        {
            WarpBackendKind.CoreCLR => "coreclr",
            WarpBackendKind.NVPTX => "nvptx",
            WarpBackendKind.AMDGPU => "amdgpu",
            WarpBackendKind.SPIRV => "spirv",
            _ => throw new ArgumentOutOfRangeException(nameof(backend)),
        };
        string expectedFormat = backend switch
        {
            WarpBackendKind.CoreCLR => "coreclr-plan",
            WarpBackendKind.NVPTX => "nvptx",
            WarpBackendKind.AMDGPU => "amdgpu-llvm-ir",
            WarpBackendKind.SPIRV => "spirv-llvm-ir",
            _ => throw new ArgumentOutOfRangeException(nameof(backend)),
        };

        Assert.AreEqual(expectedName, WarpArtifactSidecarCodec.GetBackendName(backend), StringComparer.Ordinal);
        Assert.AreEqual(
            expectedFormat,
            WarpArtifactSidecarCodec.GetFormatName(
                WarpArtifactFormatCatalog.ForBackend(backend)), StringComparer.Ordinal);
    }

    [TestMethod]
    [FourBackends]
    public void PackageBindsEveryArtifactHash(WarpBackendKind backend)
    {
        WarpAotPackage package = new WarpBuildPipeline().CompilePackage(
            ManifestAssemblyFixture.ReadAssembly());

        Assert.HasCount(8, package.Artifacts);
        Assert.HasCount(16, package.Files);
        WarpPackagedArtifact artifact = package.Artifacts.SingleItem(
            candidate => candidate.Sidecar.Backend == backend &&
                string.Equals(candidate.Sidecar.Entry, ManifestAssemblyFixture.MapEntryIdentity, StringComparison.Ordinal));
        Assert.AreEqual(WarpProfileCatalog.ProfileId, artifact.Sidecar.Profile, StringComparer.Ordinal);
        Assert.AreEqual(WarpDeviceAbi.Version, artifact.Sidecar.DeviceAbi, StringComparer.Ordinal);
        Assert.AreEqual(WarpArtifactFormatCatalog.ForBackend(backend), artifact.Sidecar.Format);

        ReadOnlyMemory<byte> sidecarBytes = package.Files[artifact.SidecarPath];
        WarpArtifactSidecar decoded = WarpArtifactSidecarCodec.Deserialize(sidecarBytes.Span);
        Assert.AreEqual(artifact.Sidecar, decoded);
        CollectionAssert.AreEqual(
            WarpArtifactSidecarCodec.Serialize(decoded),
            sidecarBytes.ToArray());

        WarpPackagedArtifact reductionArtifact = package.Artifacts.SingleItem(
            candidate => candidate.Sidecar.Backend == backend &&
                string.Equals(candidate.Sidecar.Entry, ManifestAssemblyFixture.ReductionEntryIdentity, StringComparison.Ordinal));
        Assert.AreEqual(
            ManifestAssemblyFixture.ReductionGraphHash,
            reductionArtifact.Sidecar.GraphHash, StringComparer.Ordinal);
        Assert.AreEqual(WarpArtifactFormatCatalog.ForBackend(backend), reductionArtifact.Sidecar.Format);
    }

    [TestMethod]
    [FourBackends]
    public void PackagePathsAndBytesAreDeterministic(WarpBackendKind backend)
    {
        var pipeline = new WarpBuildPipeline();
        byte[] assembly = ManifestAssemblyFixture.ReadAssembly();
        WarpAotPackage first = pipeline.CompilePackage(assembly);
        WarpAotPackage second = pipeline.CompilePackage(assembly);

        WarpPackagedArtifact firstArtifact = first.Artifacts.SingleItem(
            candidate => candidate.Sidecar.Backend == backend &&
                string.Equals(candidate.Sidecar.Entry, ManifestAssemblyFixture.MapEntryIdentity, StringComparison.Ordinal));
        WarpPackagedArtifact secondArtifact = second.Artifacts.SingleItem(
            candidate => candidate.Sidecar.Backend == backend &&
                string.Equals(candidate.Sidecar.Entry, ManifestAssemblyFixture.MapEntryIdentity, StringComparison.Ordinal));
        Assert.AreEqual(firstArtifact, secondArtifact);
        CollectionAssert.AreEqual(
            first.Files.Keys.ToArray(),
            second.Files.Keys.ToArray());

        foreach (string path in first.Files.Keys)
        {
            CollectionAssert.AreEqual(
                first.Files[path].ToArray(),
                second.Files[path].ToArray());
        }
    }

    [TestMethod]
    [FourBackends]
    public void WrittenPackageValidatesFromDisk(WarpBackendKind backend)
    {
        WarpAotPackage package = new WarpBuildPipeline().CompilePackage(
            ManifestAssemblyFixture.ReadAssembly());
        string directory = CreateTestDirectory(backend);

        try
        {
            package.WriteToDirectory(directory);
            package.ValidateDirectory(directory);

            WarpPackagedArtifact artifact = package.Artifacts.SingleItem(
                candidate => candidate.Sidecar.Backend == backend &&
                    string.Equals(candidate.Sidecar.Entry, ManifestAssemblyFixture.MapEntryIdentity, StringComparison.Ordinal));
            Assert.IsTrue(File.Exists(Path.Combine(directory, artifact.ModulePath)));
            Assert.IsTrue(File.Exists(Path.Combine(directory, artifact.SidecarPath)));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    internal static string CreateTestDirectory(WarpBackendKind backend) => Path.Combine(
        Path.GetTempPath(),
        "WarpCLR.Tests",
        $"{nameof(AotPackagingFeatureTests)}-{backend}-{Guid.NewGuid():N}");
}
