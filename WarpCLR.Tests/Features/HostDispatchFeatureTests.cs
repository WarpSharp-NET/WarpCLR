using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Sdk;

namespace WarpCLR.Tests.Features;

[TestClass]
public sealed class HostDispatchFeatureTests
{
    private const string EntryIdentity = ManifestAssemblyFixture.MapEntryIdentity;

    [TestMethod]
    [FourBackends]
    public void Explicit_emulation_dispatches_the_selected_backend(WarpBackendKind backend)
    {
        byte[] assembly = ManifestAssemblyFixture.ReadAssembly();
        WarpAotPackage package = new WarpBuildPipeline().CompilePackage(assembly);
        string directory = AotPackagingFeatureTests.CreateTestDirectory(backend);

        try
        {
            package.WriteToDirectory(directory);
            WarpLoadedModule module = new WarpDevelopmentModuleLoader().Load(assembly, directory);
            var session = new WarpDevelopmentSession(
                module,
                backend,
                WarpDevelopmentExecutionMode.SemanticEmulation);
            uint[] input = [0u, 1u, uint.MaxValue, 0x80000000u, 0x12345678u];
            const uint scalar = 0xA5A5A5A5u;
            uint[] expected = input.Select(value => TestKernels.ManifestMap(value, scalar)).ToArray();

            uint[] output = session.DispatchIntegerMap(
                EntryIdentity,
                [input],
                [scalar]);

            Assert.AreEqual(backend, session.Backend);
            Assert.AreEqual(WarpDevelopmentExecutionMode.SemanticEmulation, session.Mode);
            CollectionAssert.AreEqual(expected, output);
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
    public void Empty_dispatch_returns_an_empty_output(WarpBackendKind backend)
    {
        byte[] assembly = ManifestAssemblyFixture.ReadAssembly();
        WarpAotPackage package = new WarpBuildPipeline().CompilePackage(assembly);
        string directory = AotPackagingFeatureTests.CreateTestDirectory(backend);

        try
        {
            package.WriteToDirectory(directory);
            WarpLoadedModule module = new WarpDevelopmentModuleLoader().Load(assembly, directory);
            var session = new WarpDevelopmentSession(
                module,
                backend,
                WarpDevelopmentExecutionMode.SemanticEmulation);

            uint[] output = session.DispatchIntegerMap(
                EntryIdentity,
                [Array.Empty<uint>()],
                [7u]);

            Assert.IsEmpty(output);
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
