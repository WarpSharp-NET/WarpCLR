using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Runtime.Host.Native;

namespace WarpCLR.Tests.Architecture;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest creates this internal fixture through reflected discovery.")]
internal sealed class ZCacheShutdownAggregationTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task MixedShutdownFailuresExposeTheCompilerBugToEverySharedWaiter(bool resourceFirst)
    {
        var cache = new WarpJitCache(new WarpJitCacheOptions { MaximumConcurrentCompilations = 3 });
        bool verified = false;
        try
        {
            await using var cacheOwner = cache.ConfigureAwait(false);
            await VerifyAsync(cache, resourceFirst).ConfigureAwait(false);
            verified = true;
        }
        catch (AggregateException) { Assert.IsTrue(verified); }
        Assert.IsTrue(verified);
    }

    private static async Task VerifyAsync(WarpJitCache cache, bool resourceFirst)
    {
        WarpRuntimeModule module = LoadModule();
        WarpRuntimeEntry entry = module.Entries[ManifestAssemblyFixture.MapEntryIdentity];
        var target = new WarpNativeTarget(WarpBackendKind.NVPTX, "sm_80", "fixture", "fixture", 256, uint.MaxValue, 1UL << 32);
        using var started = new SemaphoreSlim(0, 3);
        using var resource = new SemaphoreSlim(0, 1);
        using var compiler = new SemaphoreSlim(0, 1);
        async Task<WarpNativeImage> CompileAsync(int kind, CancellationToken token)
        {
            started.Release();
            if (kind == 2) { await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false); }
            await (kind == 0 ? resource : compiler).WaitAsync(CancellationToken.None).ConfigureAwait(false);
            if (kind == 0) { throw new WarpCompilationResourceException("fixture", WarpCompilationResourceKind.SourceBytes, 2, 1); }
            throw new InvalidOperationException("Unexpected mixed compiler programming fault.");
        }

        Task<WarpNativeImage> expected = cache.GetOrCompileNativeAsync(module, entry, target, "resource", token => CompileAsync(0, token), CancellationToken.None);
        Task<WarpNativeImage> unexpected = cache.GetOrCompileNativeAsync(module, entry, target, "compiler", token => CompileAsync(1, token), CancellationToken.None);
        Task<WarpNativeImage> cancelled = cache.GetOrCompileNativeAsync(module, entry, target, "cancellation", token => CompileAsync(2, token), CancellationToken.None);
        for (int index = 0; index < 3; index++) { Assert.IsTrue(await started.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false)); }
        Task shutdown = cache.ShutdownOwnedAsync().AsTask();
        Assert.AreSame(shutdown, cache.ShutdownOwnedAsync().AsTask());
        if (resourceFirst)
        {
            resource.Release();
            try { await expected.ConfigureAwait(false); Assert.Fail("The resource failure was hidden."); }
            catch (WarpCompilationResourceException) { Assert.IsTrue(expected.IsFaulted); }
            compiler.Release();
        }
        else
        {
            compiler.Release();
            try { await unexpected.ConfigureAwait(false); Assert.Fail("The compiler failure was hidden."); }
            catch (InvalidOperationException) { Assert.IsTrue(unexpected.IsFaulted); }
            resource.Release();
        }
        await VerifySharedFailureAsync(cache).ConfigureAwait(false);
        Assert.IsInstanceOfType<WarpCompilationResourceException>(expected.Exception!.GetBaseException());
        Assert.IsInstanceOfType<InvalidOperationException>(unexpected.Exception!.GetBaseException());
        try { await cancelled.ConfigureAwait(false); Assert.Fail("The lifetime cancellation was hidden."); }
        catch (OperationCanceledException) { Assert.IsTrue(cancelled.IsCanceled); }
    }

    private static async Task VerifySharedFailureAsync(WarpJitCache cache)
    {
        AggregateException failure = await Assert.ThrowsExactlyAsync<AggregateException>(() => cache.ShutdownOwnedAsync().AsTask()).ConfigureAwait(false);
        Assert.HasCount(2, failure.Flatten().InnerExceptions);
        Assert.IsTrue(failure.InnerExceptions.Any(static error => error is InvalidOperationException));
        Assert.IsTrue(failure.InnerExceptions.Any(static error => error is WarpCompilationResourceException));
        AggregateException repeat = await Assert.ThrowsExactlyAsync<AggregateException>(() => cache.DisposeAsync().AsTask()).ConfigureAwait(false);
        Assert.AreSame(failure, repeat);
        Assert.AreEqual(0, cache.Statistics.MemoryEntryCount);
    }

    private static WarpRuntimeModule LoadModule()
    {
        byte[] bytes = ManifestAssemblyFixture.ReadAssembly();
        return WarpRuntimeModule.Load(bytes, new WarpModuleTrust([Convert.ToHexString(SHA256.HashData(bytes))]));
    }
}
