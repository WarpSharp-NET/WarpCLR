using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Sdk;

namespace WarpCLR.Tests.Regressions;

[TestClass]
[global::System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest creates this internal fixture through reflected discovery.")]
internal sealed class ReductionRegressionTests
{
    [TestMethod]
    [FourBackends]
    public void ReductionArtifactsAreDeterministic(WarpBackendKind backend)
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
