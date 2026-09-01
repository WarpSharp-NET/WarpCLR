using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Regressions;

[TestClass]
public sealed class ManifestRegressionTests
{
    [TestMethod]
    [FourBackends]
    public void Missing_embedded_manifest_is_rejected(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        byte[] assembly = ManifestAssemblyFixture.ReplaceUtf8(
            ManifestAssemblyFixture.ReadAssembly(),
            "WarpCIL.Manifest",
            "WarpCIL.ManifesX");

        WarpVerificationException exception = Assert.ThrowsExactly<WarpVerificationException>(
            () => new WarpModuleVerifier().Verify(assembly));

        Assert.AreEqual("WRPCIL2000", exception.Code);
        StringAssert.Contains(exception.Message, "does not contain");
    }

    [TestMethod]
    [FourBackends]
    public void Stale_graph_hash_is_rejected(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        string staleHash = $"8{ManifestAssemblyFixture.MapGraphHash[1..]}";
        byte[] assembly = ManifestAssemblyFixture.ReplaceUtf8(
            ManifestAssemblyFixture.ReadAssembly(),
            ManifestAssemblyFixture.MapGraphHash,
            staleHash);

        WarpVerificationException exception = Assert.ThrowsExactly<WarpVerificationException>(
            () => new WarpModuleVerifier().Verify(assembly));

        Assert.AreEqual("WRPCIL2004", exception.Code);
        StringAssert.Contains(exception.Message, ManifestAssemblyFixture.MapGraphHash);
        StringAssert.Contains(exception.Message, staleHash);
    }

    [TestMethod]
    [FourBackends]
    public void Noncanonical_graph_hash_is_rejected(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        byte[] assembly = ManifestAssemblyFixture.ReplaceUtf8(
            ManifestAssemblyFixture.ReadAssembly(),
            ManifestAssemblyFixture.MapGraphHash,
            ManifestAssemblyFixture.MapGraphHash.ToLowerInvariant());

        WarpVerificationException exception = Assert.ThrowsExactly<WarpVerificationException>(
            () => new WarpModuleVerifier().Verify(assembly));

        Assert.AreEqual("WRPCIL2001", exception.Code);
        StringAssert.Contains(exception.Message, "uppercase SHA-256");
    }

    [TestMethod]
    [FourBackends]
    public void Unapproved_capability_version_is_rejected(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        string capabilityAndHash =
            $"warp.memory.scoped/0.1\"],\"graphHash\":\"{ManifestAssemblyFixture.MapGraphHash}";
        string changedCapabilityAndHash =
            $"warp.memory.scoped/0.2\"],\"graphHash\":\"{ManifestAssemblyFixture.MapGraphHash}";
        byte[] assembly = ManifestAssemblyFixture.ReplaceUtf8(
            ManifestAssemblyFixture.ReadAssembly(),
            capabilityAndHash,
            changedCapabilityAndHash);

        WarpVerificationException exception = Assert.ThrowsExactly<WarpVerificationException>(
            () => new WarpModuleVerifier().Verify(assembly));

        Assert.AreEqual("WRPCIL2002", exception.Code);
        StringAssert.Contains(exception.Message, "exact profile capabilities");
    }

    [TestMethod]
    [FourBackends]
    public void Unknown_execution_mode_is_rejected(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        byte[] assembly = ManifestAssemblyFixture.ReplaceUtf8(
            ManifestAssemblyFixture.ReadAssembly(),
            "reduce-wrapping-sum",
            "reduce-wrapping-bad");

        WarpVerificationException exception = Assert.ThrowsExactly<WarpVerificationException>(
            () => new WarpModuleVerifier().Verify(assembly));

        Assert.AreEqual("WRPCIL2001", exception.Code);
        StringAssert.Contains(exception.Message, "execution mode");
    }
}
