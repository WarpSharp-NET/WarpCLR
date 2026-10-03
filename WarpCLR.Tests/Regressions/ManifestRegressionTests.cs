using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Regressions;

[TestClass]
[global::System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest creates this internal fixture through reflected discovery.")]
internal sealed class ManifestRegressionTests
{
    [TestMethod]
    [FourBackends]
    public void MissingEmbeddedManifestIsRejected(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        byte[] assembly = ManifestAssemblyFixture.ReplaceUtf8(
            ManifestAssemblyFixture.ReadAssembly(),
            "WarpCIL.Manifest",
            "WarpCIL.ManifesX");

        WarpVerificationException exception = Assert.ThrowsExactly<WarpVerificationException>(
            () => new WarpModuleVerifier().Verify(assembly));

        Assert.AreEqual("WRPCIL2000", exception.Code, StringComparer.Ordinal);
        StringAssert.Contains(exception.Message, "does not contain", StringComparison.Ordinal);
    }

    [TestMethod]
    [FourBackends]
    public void StaleGraphHashIsRejected(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        string staleHash = $"8{ManifestAssemblyFixture.MapGraphHash[1..]}";
        byte[] assembly = ManifestAssemblyFixture.ReplaceUtf8(
            ManifestAssemblyFixture.ReadAssembly(),
            ManifestAssemblyFixture.MapGraphHash,
            staleHash);

        WarpVerificationException exception = Assert.ThrowsExactly<WarpVerificationException>(
            () => new WarpModuleVerifier().Verify(assembly));

        Assert.AreEqual("WRPCIL2004", exception.Code, StringComparer.Ordinal);
        StringAssert.Contains(exception.Message, ManifestAssemblyFixture.MapGraphHash, StringComparison.Ordinal);
        StringAssert.Contains(exception.Message, staleHash, StringComparison.Ordinal);
    }

    [TestMethod]
    [FourBackends]
    public void NoncanonicalGraphHashIsRejected(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        byte[] assembly = ManifestAssemblyFixture.ReplaceUtf8(
            ManifestAssemblyFixture.ReadAssembly(),
            ManifestAssemblyFixture.MapGraphHash,
            ManifestAssemblyFixture.MapGraphHash.Replace('E', 'e'));

        WarpVerificationException exception = Assert.ThrowsExactly<WarpVerificationException>(
            () => new WarpModuleVerifier().Verify(assembly));

        Assert.AreEqual("WRPCIL2001", exception.Code, StringComparer.Ordinal);
        StringAssert.Contains(exception.Message, "uppercase SHA-256", StringComparison.Ordinal);
    }

    [TestMethod]
    [FourBackends]
    public void UnapprovedCapabilityVersionIsRejected(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        string capabilityAndHash =
            $"warp.core.calls/0.1\"],\"graphHash\":\"{ManifestAssemblyFixture.MapGraphHash}";
        string changedCapabilityAndHash =
            $"warp.core.calls/0.2\"],\"graphHash\":\"{ManifestAssemblyFixture.MapGraphHash}";
        byte[] assembly = ManifestAssemblyFixture.ReplaceUtf8(
            ManifestAssemblyFixture.ReadAssembly(),
            capabilityAndHash,
            changedCapabilityAndHash);

        WarpVerificationException exception = Assert.ThrowsExactly<WarpVerificationException>(
            () => new WarpModuleVerifier().Verify(assembly));

        Assert.AreEqual("WRPCIL2002", exception.Code, StringComparer.Ordinal);
        StringAssert.Contains(exception.Message, "exact profile capabilities", StringComparison.Ordinal);
    }

    [TestMethod]
    [FourBackends]
    public void UnknownExecutionModeIsRejected(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        byte[] assembly = ManifestAssemblyFixture.ReplaceUtf8(
            ManifestAssemblyFixture.ReadAssembly(),
            "reduce-wrapping-sum",
            "reduce-wrapping-bad");

        WarpVerificationException exception = Assert.ThrowsExactly<WarpVerificationException>(
            () => new WarpModuleVerifier().Verify(assembly));

        Assert.AreEqual("WRPCIL2001", exception.Code, StringComparer.Ordinal);
        StringAssert.Contains(exception.Message, "execution mode", StringComparison.Ordinal);
    }
}
