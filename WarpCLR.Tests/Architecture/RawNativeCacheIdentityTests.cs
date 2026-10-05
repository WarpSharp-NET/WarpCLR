using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Runtime.Host.Native;

namespace WarpCLR.Tests.Architecture;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest discovers and instantiates this internal fixture through reflection.")]
internal sealed class RawNativeCacheIdentityTests
{
    [TestMethod]
    public async Task NativeCacheKeepsRawDeviceRuntimeToolchainAndFieldBoundariesIndependent()
    {
        byte[] bytes = ManifestAssemblyFixture.ReadAssembly();
        WarpRuntimeModule module = WarpRuntimeModule.Load(bytes, new WarpModuleTrust([Convert.ToHexString(SHA256.HashData(bytes))]));
        WarpRuntimeEntry entry = module.Entries[ManifestAssemblyFixture.MapEntryIdentity];
        var cache = new WarpJitCache(new WarpJitCacheOptions { MaximumMemoryEntries = 32 });
        await using var cacheLease = cache.ConfigureAwait(false);
        foreach (string name in new[] { "identity_\uD800", "identity_\uD801", "identity_\uDC00", "identity_\uDC01", "identity_\uFFFD", "identity_\uD800\uDC00", "identity_\0" })
        {
            await ImageAsync(cache, module, entry, Target(name, "runtime"), "toolchain").ConfigureAwait(false);
            await ImageAsync(cache, module, entry, Target("device", name), "toolchain").ConfigureAwait(false);
            await ImageAsync(cache, module, entry, Target("device", "runtime"), name).ConfigureAwait(false);
        }
        await ImageAsync(cache, module, entry, Target("device\nruntime", "tail"), "toolchain").ConfigureAwait(false);
        await ImageAsync(cache, module, entry, Target("device", "runtime\ntail"), "toolchain").ConfigureAwait(false);
        await ImageAsync(cache, module, entry, Target("identity_\uD800", "runtime"), "toolchain").ConfigureAwait(false);
        Assert.AreEqual(23L, cache.Statistics.CompilationCount);
        Assert.AreEqual(1L, cache.Statistics.MemoryHitCount);
        Assert.AreEqual(23, cache.Statistics.MemoryEntryCount);
    }

    // This fixture exercises cache admission and identity only; its placeholder image is never executed.
    private static Task<WarpNativeImage> ImageAsync(WarpJitCache cache, WarpRuntimeModule module, WarpRuntimeEntry entry,
        WarpNativeTarget target, string toolchain) => cache.GetOrCompileNativeAsync(module, entry, target, toolchain,
            _ => Task.FromResult(new WarpNativeImage(target, WarpNativeImageFormat.Ptx, WarpPortableMachineEmitter.EntryPoint,
                [1], "identity-fixture-source", toolchain, entry.InputBufferCount, entry.ScalarArgumentCount, entry.Layout)), CancellationToken.None);

    private static WarpNativeTarget Target(string device, string runtime) => new(WarpBackendKind.NVPTX, "sm_80", device, runtime, 64, 1024, 1UL << 30);
}
