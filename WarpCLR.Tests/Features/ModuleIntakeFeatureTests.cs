using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Sdk;

namespace WarpCLR.Tests.Features;

[TestClass]
[global::System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest creates this internal fixture through reflected discovery.")]
internal sealed class ModuleIntakeFeatureTests
{
    [TestMethod]
    [FourBackends]
    public void EmbeddedManifestCompilesWithoutLoadingTheAssembly(WarpBackendKind backend)
    {
        byte[] assemblyBytes = File.ReadAllBytes(typeof(TestKernels).Assembly.Location);
        WarpModuleCompilation module = new WarpBuildPipeline().CompileModule(assemblyBytes);

        Assert.HasCount(2, module.Module.Entries);
        Assert.AreEqual("WarpCLR.Tests", module.Module.Producer, StringComparer.Ordinal);
        Assert.AreEqual("0.1.0", module.Module.ProducerVersion, StringComparer.Ordinal);
        Assert.AreEqual(64, module.Module.ManifestHash.Length);
        Assert.AreEqual(64, module.Module.AssemblyHash.Length);

        const string identity = ManifestAssemblyFixture.MapEntryIdentity;
        Assert.IsTrue(module.Entries.TryGetValue(identity, out WarpCompilation? compilation));
        Assert.IsNotNull(compilation);
        BackendArtifactAssertions.IsValid(
            compilation.Artifacts[backend],
            backend,
            compilation.Kernel);

        Assert.IsTrue(module.Entries.TryGetValue(
            ManifestAssemblyFixture.ReductionEntryIdentity,
            out WarpCompilation? reduction));
        Assert.IsNotNull(reduction);
        Assert.AreEqual(WarpReductionOperation.WrappingSum, reduction.Kernel.Reduction);
        BackendArtifactAssertions.IsValid(
            reduction.Artifacts[backend],
            backend,
            reduction.Kernel);
    }
}
