using WarpCLR.IR;
using WarpCLR.Sdk;

namespace WarpCLR.Tests.Features;

[TestClass]
public sealed class AotPackagingFeatureTests
{
    [TestMethod]
    [FourBackends]
    public void Package_binds_every_artifact_hash(WarpBackendKind backend)
    {
        WarpAotPackage package = new WarpBuildPipeline().CompilePackage(
            ManifestAssemblyFixture.ReadAssembly());

        Assert.HasCount(4, package.Artifacts);
        Assert.HasCount(8, package.Files);
        WarpPackagedArtifact artifact = package.Artifacts.Single(
            candidate => candidate.Sidecar.Backend == backend);
        Assert.AreEqual(WarpProfileCatalog.ProfileId, artifact.Sidecar.Profile);
        Assert.AreEqual(WarpDeviceAbi.Version, artifact.Sidecar.DeviceAbi);
        Assert.AreEqual(WarpArtifactFormatCatalog.ForBackend(backend), artifact.Sidecar.Format);

        ReadOnlyMemory<byte> sidecarBytes = package.Files[artifact.SidecarPath];
        WarpArtifactSidecar decoded = WarpArtifactSidecarCodec.Deserialize(sidecarBytes.Span);
        Assert.AreEqual(artifact.Sidecar, decoded);
        CollectionAssert.AreEqual(
            WarpArtifactSidecarCodec.Serialize(decoded),
            sidecarBytes.ToArray());
    }

    [TestMethod]
    [FourBackends]
    public void Package_paths_and_bytes_are_deterministic(WarpBackendKind backend)
    {
        var pipeline = new WarpBuildPipeline();
        byte[] assembly = ManifestAssemblyFixture.ReadAssembly();
        WarpAotPackage first = pipeline.CompilePackage(assembly);
        WarpAotPackage second = pipeline.CompilePackage(assembly);

        WarpPackagedArtifact firstArtifact = first.Artifacts.Single(
            candidate => candidate.Sidecar.Backend == backend);
        WarpPackagedArtifact secondArtifact = second.Artifacts.Single(
            candidate => candidate.Sidecar.Backend == backend);
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
    public void Written_package_validates_from_disk(WarpBackendKind backend)
    {
        WarpAotPackage package = new WarpBuildPipeline().CompilePackage(
            ManifestAssemblyFixture.ReadAssembly());
        string directory = CreateTestDirectory(backend);

        try
        {
            package.WriteToDirectory(directory);
            package.ValidateDirectory(directory);

            WarpPackagedArtifact artifact = package.Artifacts.Single(
                candidate => candidate.Sidecar.Backend == backend);
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
