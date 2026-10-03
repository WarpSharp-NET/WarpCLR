using WarpCLR.IR;
using WarpCLR.Sdk;

namespace WarpCLR.Tests.Regressions;

[TestClass]
[global::System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest creates this internal fixture through reflected discovery.")]
internal sealed class AotPackageRegressionTests
{
    [TestMethod]
    [FourBackends]
    public void ChangedModuleBytesAreRejected(WarpBackendKind backend)
    {
        WarpAotPackage package = new WarpBuildPipeline().CompilePackage(
            ManifestAssemblyFixture.ReadAssembly());
        string directory = Features.AotPackagingFeatureTests.CreateTestDirectory(backend);

        try
        {
            package.WriteToDirectory(directory);
            WarpPackagedArtifact artifact = package.Artifacts.SingleItem(
                candidate => candidate.Sidecar.Backend == backend &&
                    string.Equals(candidate.Sidecar.Entry, ManifestAssemblyFixture.MapEntryIdentity, StringComparison.Ordinal));
            string modulePath = Path.Combine(directory, artifact.ModulePath);
            byte[] module = File.ReadAllBytes(modulePath);
            module[0] ^= 0x01;
            File.WriteAllBytes(modulePath, module);

            InvalidDataException exception = Assert.ThrowsExactly<InvalidDataException>(
                () => package.ValidateDirectory(directory));

            StringAssert.Contains(exception.Message, "changed hash", StringComparison.Ordinal);
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
    public void ChangedSidecarBytesAreRejected(WarpBackendKind backend)
    {
        WarpAotPackage package = new WarpBuildPipeline().CompilePackage(
            ManifestAssemblyFixture.ReadAssembly());
        string directory = Features.AotPackagingFeatureTests.CreateTestDirectory(backend);

        try
        {
            package.WriteToDirectory(directory);
            WarpPackagedArtifact artifact = package.Artifacts.SingleItem(
                candidate => candidate.Sidecar.Backend == backend &&
                    string.Equals(candidate.Sidecar.Entry, ManifestAssemblyFixture.MapEntryIdentity, StringComparison.Ordinal));
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

            StringAssert.Contains(exception.Message, "package index", StringComparison.Ordinal);
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
