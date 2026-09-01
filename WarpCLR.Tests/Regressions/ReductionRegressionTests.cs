using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Sdk;

namespace WarpCLR.Tests.Regressions;

[TestClass]
public sealed class ReductionRegressionTests
{
    [TestMethod]
    [FourBackends]
    public void Reduction_artifacts_are_deterministic(WarpBackendKind backend)
    {
        var pipeline = new WarpBuildPipeline();
        byte[] assembly = ManifestAssemblyFixture.ReadAssembly();

        WarpCompilation first = pipeline
            .CompileModule(assembly)
            .Entries[ManifestAssemblyFixture.ReductionEntryIdentity];
        WarpCompilation second = pipeline
            .CompileModule(assembly)
            .Entries[ManifestAssemblyFixture.ReductionEntryIdentity];

        BackendArtifactAssertions.IsValid(
            first.Artifacts[backend],
            backend,
            first.Kernel);
        CollectionAssert.AreEqual(
            first.Artifacts[backend].Content.ToArray(),
            second.Artifacts[backend].Content.ToArray());
    }
}
