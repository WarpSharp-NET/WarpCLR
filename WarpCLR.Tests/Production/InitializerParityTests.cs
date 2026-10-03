using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest discovers and instantiates this fixture through reflection.")]
internal sealed class InitializerParityTests
{
    private static readonly string[] RejectedScenarios =
        ["entry-initializer", "helper-initializer", "module-initializer", "synchronized-entry", "synchronized-helper"];

    [TestMethod]
    [FourBackends]
    public void ReachableEntryInitializerIsRejectedWithoutExecution(WarpBackendKind backend)
        => AssertReflectionRejected("entry-initializer", "WRPCIL1015", backend);

    [TestMethod]
    [FourBackends]
    public void ReachableHelperInitializerIsRejectedWithoutExecution(WarpBackendKind backend)
        => AssertReflectionRejected("helper-initializer", "WRPCIL1015", backend);

    [TestMethod]
    [FourBackends]
    public void GlobalModuleInitializerIsRejectedFromReflectionMetadata(WarpBackendKind backend)
        => AssertReflectionRejected("module-initializer", "WRPCIL1015", backend);

    [TestMethod]
    [FourBackends]
    public void SynchronizedEntryIsRejectedOnEveryBackend(WarpBackendKind backend)
        => AssertReflectionRejected("synchronized-entry", "WRPCIL1016", backend);

    [TestMethod]
    [FourBackends]
    public void SynchronizedHelperIsRejectedOnEveryBackend(WarpBackendKind backend)
        => AssertReflectionRejected("synchronized-helper", "WRPCIL1016", backend);

    [TestMethod]
    [FourBackends]
    public async Task TrustedPeRejectsAllImplicitInitializationAndMonitorCases(WarpBackendKind backend)
    {
        foreach (string scenario in RejectedScenarios)
        {
            using var fixture = new WarpInitializerProofFixture(scenario);
            var trust = new WarpModuleTrust([Convert.ToHexString(SHA256.HashData(fixture.Bytes))]);
            WarpVerificationException error = await Assert.ThrowsExactlyAsync<WarpVerificationException>(async () =>
            {
                WarpRuntimeModule module = WarpRuntimeModule.Load(fixture.Bytes, trust);
                var context = new WarpRuntimeContext(module, backend);
                await using (context.ConfigureAwait(false))
                {
                    Assert.AreEqual(backend, context.Backend);
                }
            }).ConfigureAwait(false);
            string expected = scenario.StartsWith("synchronized", StringComparison.Ordinal) ? "WRPCIL1016" : "WRPCIL1015";
            Assert.AreEqual(expected, error.Code, StringComparer.Ordinal);
        }
    }

    [TestMethod]
    [FourBackends]
    public async Task UnrelatedHostInitializerIsAcceptedAndNeverRun(WarpBackendKind backend)
    {
        using var fixture = new WarpInitializerProofFixture("unrelated-initializer");
        uint[] emulated = KernelTestHarness.CompileAndEmulate(fixture.Method, 1, backend, [[17]], [5]);
        Assert.HasCount(1, emulated);
        Assert.AreEqual(22u, emulated[0]);
        var trust = new WarpModuleTrust([Convert.ToHexString(SHA256.HashData(fixture.Bytes))]);
        WarpRuntimeModule module = WarpRuntimeModule.Load(fixture.Bytes, trust);
        var context = new WarpRuntimeContext(module, backend);
        await using (context.ConfigureAwait(false))
        {
            Assert.AreEqual(backend, context.Backend);
            Assert.AreEqual(WarpRuntimeContextState.Ready, context.State);
            if (backend == WarpBackendKind.CoreCLR)
            {
                uint[] actual = await context.DispatchIntegerMapAsync(WarpInitializerProofFixture.EntryIdentity, [[17]], [5]).ConfigureAwait(false);
                Assert.HasCount(1, actual);
                Assert.AreEqual(22u, actual[0]);
            }
        }
    }

    private static void AssertReflectionRejected(string scenario, string code, WarpBackendKind backend)
    {
        using var fixture = new WarpInitializerProofFixture(scenario);
        WarpVerificationException error = Assert.ThrowsExactly<WarpVerificationException>(
            () => KernelTestHarness.CompileAndEmulate(fixture.Method, 1, backend, [[17]], [5]));
        Assert.AreEqual(code, error.Code, StringComparer.Ordinal);
    }
}
