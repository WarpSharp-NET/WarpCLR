using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Sdk;

namespace WarpCLR.Tests.Regressions;

[TestClass]
[global::System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest creates this internal fixture through reflected discovery.")]
internal sealed class HostDispatchRegressionTests
{
    private static readonly uint[] SingleInput = [1u];
    private const string EntryIdentity = ManifestAssemblyFixture.MapEntryIdentity;

    [TestMethod]
    [FourBackends]
    public void MissingBackendPreventsPackageLoading(WarpBackendKind backend)
    {
        byte[] assembly = ManifestAssemblyFixture.ReadAssembly();
        WarpAotPackage package = new WarpBuildPipeline().CompilePackage(assembly);
        string directory = Features.AotPackagingFeatureTests.CreateTestDirectory(backend);

        try
        {
            package.WriteToDirectory(directory);
            WarpPackagedArtifact artifact = package.Artifacts.SingleItem(
                candidate => candidate.Sidecar.Backend == backend &&
                    string.Equals(candidate.Sidecar.Entry, ManifestAssemblyFixture.MapEntryIdentity, StringComparison.Ordinal));
            File.Delete(Path.Combine(directory, artifact.SidecarPath));

            WarpHostException exception = Assert.ThrowsExactly<WarpHostException>(
                () => new WarpDevelopmentModuleLoader().Load(assembly, directory));

            Assert.AreEqual("WRPHOST1000", exception.Code, StringComparer.Ordinal);
            StringAssert.Contains(exception.Message, backend.ToString(), StringComparison.Ordinal);
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
    public void ImplicitOrUnknownExecutionModeIsRejected(WarpBackendKind backend)
    {
        WarpLoadedModule module = LoadModule(backend, out string directory);

        try
        {
            WarpHostException exception = Assert.ThrowsExactly<WarpHostException>(
                () => new WarpDevelopmentSession(
                    module,
                    backend,
                    (WarpDevelopmentExecutionMode)int.MaxValue));

            Assert.AreEqual("WRPHOST1003", exception.Code, StringComparer.Ordinal);
            StringAssert.Contains(exception.Message, "not registered", StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [FourBackends]
    public void UnknownEntryIsRejectedWithoutFallback(WarpBackendKind backend)
    {
        WarpLoadedModule module = LoadModule(backend, out string directory);

        try
        {
            var session = new WarpDevelopmentSession(
                module,
                backend,
                WarpDevelopmentExecutionMode.SemanticEmulation);

            WarpHostException exception = Assert.ThrowsExactly<WarpHostException>(
                () => session.DispatchIntegerMap("missing.entry", [SingleInput], [2u]));

            Assert.AreEqual("WRPHOST1001", exception.Code, StringComparer.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [FourBackends]
    public void InvalidBufferShapeIsRejectedBeforeDispatch(WarpBackendKind backend)
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

            Assert.AreEqual("WRPHOST1004", exception.Code, StringComparer.Ordinal);
            StringAssert.Contains(exception.Message, "input buffer count", StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [FourBackends]
    public void MapDispatchRejectsAReductionEntry(WarpBackendKind backend)
    {
        WarpLoadedModule module = LoadModule(backend, out string directory);

        try
        {
            var session = new WarpDevelopmentSession(
                module,
                backend,
                WarpDevelopmentExecutionMode.SemanticEmulation);

            WarpHostException exception = Assert.ThrowsExactly<WarpHostException>(
                () => session.DispatchIntegerMap(
                    ManifestAssemblyFixture.ReductionEntryIdentity,
                    [SingleInput],
                    [2u]));

            Assert.AreEqual("WRPHOST1005", exception.Code, StringComparer.Ordinal);
            StringAssert.Contains(exception.Message, "not a map", StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [FourBackends]
    public void ReductionDispatchRejectsAMapEntry(WarpBackendKind backend)
    {
        WarpLoadedModule module = LoadModule(backend, out string directory);

        try
        {
            var session = new WarpDevelopmentSession(
                module,
                backend,
                WarpDevelopmentExecutionMode.SemanticEmulation);

            WarpHostException exception = Assert.ThrowsExactly<WarpHostException>(
                () => session.DispatchUInt32Reduction(
                    EntryIdentity,
                    [SingleInput],
                    [2u]));

            Assert.AreEqual("WRPHOST1005", exception.Code, StringComparer.Ordinal);
            StringAssert.Contains(exception.Message, "not a reduction", StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [FourBackends]
    public void InvalidReductionArgumentsAreRejectedBeforeDispatch(WarpBackendKind backend)
    {
        WarpLoadedModule module = LoadModule(backend, out string directory);

        try
        {
            var session = new WarpDevelopmentSession(
                module,
                backend,
                WarpDevelopmentExecutionMode.SemanticEmulation);

            WarpHostException exception = Assert.ThrowsExactly<WarpHostException>(
                () => session.DispatchUInt32Reduction(
                    ManifestAssemblyFixture.ReductionEntryIdentity,
                    [SingleInput],
                    []));

            Assert.AreEqual("WRPHOST1004", exception.Code, StringComparer.Ordinal);
            StringAssert.Contains(exception.Message, "scalar argument count", StringComparison.Ordinal);
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
