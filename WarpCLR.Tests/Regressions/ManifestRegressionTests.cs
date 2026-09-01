using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Regressions;

[TestClass]
public sealed class ManifestRegressionTests
{
    private const string GraphHash =
        "73EA6961D7383318BED4980E9B8EC8489C4322FC765E251A05001A5C08B9FDA0";

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
        string staleHash = $"8{GraphHash[1..]}";
        byte[] assembly = ManifestAssemblyFixture.ReplaceUtf8(
            ManifestAssemblyFixture.ReadAssembly(),
            GraphHash,
            staleHash);

        WarpVerificationException exception = Assert.ThrowsExactly<WarpVerificationException>(
            () => new WarpModuleVerifier().Verify(assembly));

        Assert.AreEqual("WRPCIL2004", exception.Code);
        StringAssert.Contains(exception.Message, GraphHash);
        StringAssert.Contains(exception.Message, staleHash);
    }

    [TestMethod]
    [FourBackends]
    public void Noncanonical_graph_hash_is_rejected(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        byte[] assembly = ManifestAssemblyFixture.ReplaceUtf8(
            ManifestAssemblyFixture.ReadAssembly(),
            GraphHash,
            GraphHash.ToLowerInvariant());

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
        byte[] assembly = ManifestAssemblyFixture.ReplaceUtf8(
            ManifestAssemblyFixture.ReadAssembly(),
            "warp.memory.scoped/0.1",
            "warp.memory.scoped/0.2");

        WarpVerificationException exception = Assert.ThrowsExactly<WarpVerificationException>(
            () => new WarpModuleVerifier().Verify(assembly));

        Assert.AreEqual("WRPCIL2002", exception.Code);
        StringAssert.Contains(exception.Message, "exact profile capabilities");
    }
}
