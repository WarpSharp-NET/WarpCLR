using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Sdk;

namespace WarpCLR.Tests.Regressions;

[TestClass]
public sealed class HostDispatchRegressionTests
{
    private const string EntryIdentity = "WarpCLR.Tests.TestKernels.ManifestMap";

    [TestMethod]
    [FourBackends]
    public void Missing_backend_prevents_package_loading(WarpBackendKind backend)
    {
        byte[] assembly = ManifestAssemblyFixture.ReadAssembly();
        WarpAotPackage package = new WarpBuildPipeline().CompilePackage(assembly);
        string directory = Features.AotPackagingFeatureTests.CreateTestDirectory(backend);

        try
        {
            package.WriteToDirectory(directory);
            WarpPackagedArtifact artifact = package.Artifacts.Single(
                candidate => candidate.Sidecar.Backend == backend);
            File.Delete(Path.Combine(directory, artifact.SidecarPath));

            WarpHostException exception = Assert.ThrowsExactly<WarpHostException>(
                () => new WarpDevelopmentModuleLoader().Load(assembly, directory));

            Assert.AreEqual("WRPHOST1000", exception.Code);
            StringAssert.Contains(exception.Message, backend.ToString());
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
    public void Implicit_or_unknown_execution_mode_is_rejected(WarpBackendKind backend)
    {
        WarpLoadedModule module = LoadModule(backend, out string directory);

        try
        {
            WarpHostException exception = Assert.ThrowsExactly<WarpHostException>(
                () => new WarpDevelopmentSession(
                    module,
                    backend,
                    (WarpDevelopmentExecutionMode)int.MaxValue));

            Assert.AreEqual("WRPHOST1003", exception.Code);
            StringAssert.Contains(exception.Message, "not registered");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [FourBackends]
    public void Unknown_entry_is_rejected_without_fallback(WarpBackendKind backend)
    {
        WarpLoadedModule module = LoadModule(backend, out string directory);

        try
        {
            var session = new WarpDevelopmentSession(
                module,
                backend,
                WarpDevelopmentExecutionMode.SemanticEmulation);

            WarpHostException exception = Assert.ThrowsExactly<WarpHostException>(
                () => session.DispatchIntegerMap("missing.entry", [new uint[] { 1u }], [2u]));

            Assert.AreEqual("WRPHOST1001", exception.Code);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [FourBackends]
    public void Invalid_buffer_shape_is_rejected_before_dispatch(WarpBackendKind backend)
    {
        WarpLoadedModule module = LoadModule(backend, out string directory);

        try
        {
            var session = new WarpDevelopmentSession(
                module,
                backend,
                WarpDevelopmentExecutionMode.SemanticEmulation);

            WarpHostException exception = Assert.ThrowsExactly<WarpHostException>(
                () => session.DispatchIntegerMap(EntryIdentity, [], [2u]));

            Assert.AreEqual("WRPHOST1004", exception.Code);
            StringAssert.Contains(exception.Message, "input buffer count");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static WarpLoadedModule LoadModule(
        WarpBackendKind backend,
        out string directory)
    {
        byte[] assembly = ManifestAssemblyFixture.ReadAssembly();
        WarpAotPackage package = new WarpBuildPipeline().CompilePackage(assembly);
        directory = Features.AotPackagingFeatureTests.CreateTestDirectory(backend);
        package.WriteToDirectory(directory);
        return new WarpDevelopmentModuleLoader().Load(assembly, directory);
    }
}
