using WarpCLR.IR;

namespace WarpCLR.Tests.Features;

[TestClass]
[global::System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest creates this internal fixture through reflected discovery.")]
internal sealed class ProfileContractFeatureTests
{
    [TestMethod]
    [FourBackends]
    public void ProfileDeclaresAllApprovedFeatures(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));

        WarpProfileFeature[] expected =
        [
            WarpProfileFeature.VerifiedModuleIntake,
            WarpProfileFeature.UnsignedScalar,
            WarpProfileFeature.TypedUnsignedBuffers,
            WarpProfileFeature.OneDimensionalParallelMap,
            WarpProfileFeature.ConditionalControlFlow,
            WarpProfileFeature.ControlFlowGraph,
            WarpProfileFeature.BackwardControlFlow,
            WarpProfileFeature.ClosedWorldStaticCalls,
            WarpProfileFeature.DeterministicAotPackaging,
            WarpProfileFeature.ExplicitHostDispatch,
            WarpProfileFeature.ExactUnsignedReductions,
        ];

        CollectionAssert.AreEqual(
            expected,
            WarpProfileCatalog.Features.Select(feature => feature.Feature).ToArray());
        Assert.AreEqual(expected.Length, WarpProfileCatalog.Features.Select(feature => feature.Feature).Distinct().Count());
    }

    [TestMethod]
    [FourBackends]
    public void ProfileUsesTheExactApprovedCapabilities(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));

        string[] expected =
        [
            "warp.core.scalar/0.1",
            "warp.core.parallel/0.1",
            "warp.core.buffers/0.1",
            "warp.core.control-flow/0.2",
            "warp.core.calls/0.1",
        ];

        CollectionAssert.AreEqual(expected, WarpProfileCatalog.RequiredCapabilities.ToArray());
    }

    [TestMethod]
    [FourBackends]
    public void EveryBackendDeclaresTheExactPortableContract(WarpBackendKind backend)
    {
        IWarpBackendCompiler compiler = BackendCompilerFactory.Create(backend);

        Assert.IsTrue(compiler.Contract.ExactlyMatches(WarpProfileCatalog.BackendContract));
    }

    [TestMethod]
    [FourBackends]
    public void UnsignedReductionIdentitiesAreExact(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));

        Assert.AreEqual(0u, WarpReductionContract.Reduce(WarpReductionOperation.WrappingSum, []));
        Assert.AreEqual(uint.MaxValue, WarpReductionContract.Reduce(WarpReductionOperation.Minimum, []));
        Assert.AreEqual(0u, WarpReductionContract.Reduce(WarpReductionOperation.Maximum, []));
    }

    [TestMethod]
    [FourBackends]
    public void UnsignedReductionsHaveExactResults(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));

        uint[] values = [uint.MaxValue, 7u, 0x80000000u, 1u];
        Assert.AreEqual(
            0x80000007u,
            WarpReductionContract.Reduce(WarpReductionOperation.WrappingSum, values));
        Assert.AreEqual(1u, WarpReductionContract.Reduce(WarpReductionOperation.Minimum, values));
        Assert.AreEqual(uint.MaxValue, WarpReductionContract.Reduce(WarpReductionOperation.Maximum, values));
    }
}
