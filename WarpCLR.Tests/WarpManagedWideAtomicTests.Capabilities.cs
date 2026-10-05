using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Runtime.Host.Native;

namespace WarpCLR.Tests;

internal sealed partial class WarpManagedWideAtomicTests
{
    [TestMethod]
    public async Task NativeTargetsRejectMissingWideCapabilityBeforeInvokingAnyTool()
    {
        var toolchain = new WarpNativeToolchain(new WarpNativeToolchainOptions { LlvmAssembler = "/missing/never-invoked" });
        foreach (WarpBackendKind backend in new[] { WarpBackendKind.NVPTX, WarpBackendKind.AMDGPU, WarpBackendKind.SPIRV })
        {
            string architecture = backend == WarpBackendKind.NVPTX ? "sm_80" : backend == WarpBackendKind.AMDGPU ? "gfx942" : "opencl2.2-spirv1.2";
            var unsupported = new WarpNativeTarget(backend, architecture, "capability-test", "capability-test", 256, uint.MaxValue, 1UL << 30);
            var supported = new WarpNativeTarget(backend, architecture, "capability-test", "capability-test", 256, uint.MaxValue, 1UL << 30, supportsInt64Atomics: true);
            Assert.AreNotEqual(unsupported.CacheIdentity, supported.CacheIdentity, StringComparer.Ordinal);
            foreach (WarpLogicalMachineLayout layout in WarpManagedWideAtomicKernels.Create64())
            {
                WarpHostException error = await Assert.ThrowsExactlyAsync<WarpHostException>(() => toolchain.CompileMachineAsync(layout, unsupported)).ConfigureAwait(false);
                Assert.AreEqual("WRPNATIVE1003", error.Code, StringComparer.Ordinal);
            }
        }
        var oldPtx = new WarpNativeTarget(WarpBackendKind.NVPTX, "sm_60", "capability-test", "capability-test", 256, uint.MaxValue, 1UL << 30, supportsInt64Atomics: true);
        WarpHostException old = await Assert.ThrowsExactlyAsync<WarpHostException>(() => toolchain.CompileMachineAsync(Layout("add"), oldPtx)).ConfigureAwait(false);
        Assert.AreEqual("WRPNATIVE1003", old.Code, StringComparer.Ordinal);
    }
}
