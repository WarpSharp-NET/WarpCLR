using WarpCLR.IR;

namespace WarpCLR.Tests.Features;

[TestClass]
public sealed class ProfileContractFeatureTests
{
    [TestMethod]
    [FourBackends]
    public void Profile_declares_all_eight_approved_features(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));

        WarpProfileFeature[] expected =
        [
            WarpProfileFeature.VerifiedModuleIntake,
            WarpProfileFeature.UnsignedScalar,
            WarpProfileFeature.TypedUnsignedBuffers,
            WarpProfileFeature.OneDimensionalParallelMap,
            WarpProfileFeature.ScopedManagedMemory,
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
    public void Profile_uses_the_exact_approved_capabilities(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));

        string[] expected =
        [
            "warp.core.scalar/0.1",
            "warp.core.parallel/0.1",
            "warp.core.buffers/0.1",
            "warp.memory.scoped/0.1",
        ];

        CollectionAssert.AreEqual(expected, WarpProfileCatalog.RequiredCapabilities.ToArray());
    }

    [TestMethod]
    [FourBackends]
    public void Unsigned_reduction_identities_are_exact(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));

        Assert.AreEqual(0u, WarpReductionContract.Reduce(WarpReductionOperation.WrappingSum, []));
        Assert.AreEqual(uint.MaxValue, WarpReductionContract.Reduce(WarpReductionOperation.Minimum, []));
        Assert.AreEqual(0u, WarpReductionContract.Reduce(WarpReductionOperation.Maximum, []));
    }

    [TestMethod]
    [FourBackends]
    public void Unsigned_reductions_have_exact_results(WarpBackendKind backend)
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
