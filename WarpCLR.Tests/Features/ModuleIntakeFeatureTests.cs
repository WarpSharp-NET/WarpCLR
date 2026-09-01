using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Sdk;

namespace WarpCLR.Tests.Features;

[TestClass]
public sealed class ModuleIntakeFeatureTests
{
    [TestMethod]
    [FourBackends]
    public void Embedded_manifest_compiles_without_loading_the_assembly(WarpBackendKind backend)
    {
        byte[] assemblyBytes = File.ReadAllBytes(typeof(TestKernels).Assembly.Location);
        WarpModuleCompilation module = new WarpBuildPipeline().CompileModule(assemblyBytes);

        Assert.HasCount(2, module.Module.Entries);
        Assert.AreEqual("WarpCLR.Tests", module.Module.Producer);
        Assert.AreEqual("0.1.0", module.Module.ProducerVersion);
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
