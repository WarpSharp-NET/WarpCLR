using WarpCLR.IR;
using WarpCLR.Sdk;

namespace WarpCLR.Tests.Regressions;

[TestClass]
public sealed class AotPackageRegressionTests
{
    [TestMethod]
    [FourBackends]
    public void Changed_module_bytes_are_rejected(WarpBackendKind backend)
    {
        WarpAotPackage package = new WarpBuildPipeline().CompilePackage(
            ManifestAssemblyFixture.ReadAssembly());
        string directory = Features.AotPackagingFeatureTests.CreateTestDirectory(backend);

        try
        {
            package.WriteToDirectory(directory);
            WarpPackagedArtifact artifact = package.Artifacts.Single(
                candidate => candidate.Sidecar.Backend == backend &&
                    candidate.Sidecar.Entry == ManifestAssemblyFixture.MapEntryIdentity);
            string modulePath = Path.Combine(directory, artifact.ModulePath);
            byte[] module = File.ReadAllBytes(modulePath);
            module[0] ^= 0x01;
            File.WriteAllBytes(modulePath, module);

            InvalidDataException exception = Assert.ThrowsExactly<InvalidDataException>(
                () => package.ValidateDirectory(directory));

            StringAssert.Contains(exception.Message, "changed hash");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    [FourBackends]
    public void Changed_sidecar_bytes_are_rejected(WarpBackendKind backend)
    {
        WarpAotPackage package = new WarpBuildPipeline().CompilePackage(
            ManifestAssemblyFixture.ReadAssembly());
        string directory = Features.AotPackagingFeatureTests.CreateTestDirectory(backend);

        try
        {
            package.WriteToDirectory(directory);
            WarpPackagedArtifact artifact = package.Artifacts.Single(
                candidate => candidate.Sidecar.Backend == backend &&
                    candidate.Sidecar.Entry == ManifestAssemblyFixture.MapEntryIdentity);
            string sidecarPath = Path.Combine(directory, artifact.SidecarPath);
            byte[] sidecar = File.ReadAllBytes(sidecarPath);
            string changedHash = $"{(artifact.Sidecar.ModuleHash[0] == '0' ? '1' : '0')}" +
                artifact.Sidecar.ModuleHash[1..];
            byte[] changedSidecar = ManifestAssemblyFixture.ReplaceUtf8(
                sidecar,
                artifact.Sidecar.ModuleHash,
                changedHash);
            File.WriteAllBytes(sidecarPath, changedSidecar);

            InvalidDataException exception = Assert.ThrowsExactly<InvalidDataException>(
                () => package.ValidateDirectory(directory));

            StringAssert.Contains(exception.Message, "package index");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
