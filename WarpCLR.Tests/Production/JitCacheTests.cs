using System.Diagnostics.CodeAnalysis;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers and instantiates this fixture through reflection.")]
internal sealed class JitCacheTests
{
    [TestMethod]
    public async Task CorruptDiskCacheIsNeverAnExecutionAuthorityAndIsReplacedAtomically()
    {
        string directory = CreateDirectory();
        try
        {
            WarpRuntimeModule module = RuntimeLifecycleTests.LoadModule();
            var options = new WarpJitCacheOptions { DirectoryPath = directory };
            var firstCache = new WarpJitCache(options);
            await using var firstCacheLease = firstCache.ConfigureAwait(false);
            var first = new WarpRuntimeContext(module, WarpBackendKind.CoreCLR, jitCache: firstCache);
            await using (first.ConfigureAwait(false))
            {
                uint[] output = await first.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [new uint[] { 3 }], [7]).ConfigureAwait(false);
                Assert.AreEqual(106u, output[0]);
            }

            string[] cacheFiles = Directory.GetFiles(directory, "*.wrcache");
            Assert.HasCount(1, cacheFiles);
            string path = cacheFiles[0];
            byte[] original = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            await File.WriteAllBytesAsync(path, new byte[] { 0xFF }).ConfigureAwait(false);
            var secondCache = new WarpJitCache(options);
            await using var secondCacheLease = secondCache.ConfigureAwait(false);
            var second = new WarpRuntimeContext(module, WarpBackendKind.CoreCLR, jitCache: secondCache);
            await using (second.ConfigureAwait(false))
            {
                uint[] output = await second.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [new uint[] { 3 }], [7]).ConfigureAwait(false);
                Assert.AreEqual(106u, output[0]);
                Assert.AreEqual(0, second.JitStatistics.DiskHitCount);
            }

            CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(path).ConfigureAwait(false));
            var thirdCache = new WarpJitCache(options);
            await using var thirdCacheLease = thirdCache.ConfigureAwait(false);
            var third = new WarpRuntimeContext(module, WarpBackendKind.CoreCLR, jitCache: thirdCache);
            await using var thirdLease = third.ConfigureAwait(false);
            await third.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [new uint[] { 3 }], [7]).ConfigureAwait(false);
            Assert.AreEqual(1, third.JitStatistics.DiskHitCount);
            Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task MemoryCacheEvictionReleasesOldCompilationsWithoutInvalidatingDispatch()
    {
        var cache = new WarpJitCache(new WarpJitCacheOptions { MaximumMemoryEntries = 1 });
        await using var cacheLease = cache.ConfigureAwait(false);
        var context = new WarpRuntimeContext(RuntimeLifecycleTests.LoadModule(), WarpBackendKind.CoreCLR, jitCache: cache);
        await using var contextLease = context.ConfigureAwait(false);
        await context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [new uint[] { 1 }], [0]).ConfigureAwait(false);
        await context.DispatchUInt32ReductionAsync(ManifestAssemblyFixture.ReductionEntryIdentity, [new uint[] { 1 }], [0]).ConfigureAwait(false);
        await context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [new uint[] { 1 }], [0]).ConfigureAwait(false);
        Assert.AreEqual(3, cache.Statistics.CompilationCount);
        Assert.AreEqual(1, cache.Statistics.MemoryEntryCount);
    }

    [TestMethod]
    public async Task DefaultContextDisposalClearsItsOwnedCacheWhileTheContextRemainsReachable()
    {
        var context = new WarpRuntimeContext(RuntimeLifecycleTests.LoadModule(), WarpBackendKind.CoreCLR);
        await using var contextLease = context.ConfigureAwait(false);
        uint[] output = await context.DispatchIntegerMapAsync(
            ManifestAssemblyFixture.MapEntryIdentity, [new uint[] { 3 }], [7]).ConfigureAwait(false);
        Assert.AreEqual(106u, output[0]);
        Assert.AreEqual(1, context.JitStatistics.MemoryEntryCount);
        Assert.AreEqual(1L, context.JitStatistics.CompilationCount);

        await context.DisposeAsync().ConfigureAwait(false);

        Assert.AreEqual(WarpRuntimeContextState.Disposed, context.State);
        Assert.AreEqual(0, context.JitStatistics.MemoryEntryCount);
        Assert.AreEqual(1L, context.JitStatistics.CompilationCount);
        GC.KeepAlive(context);
    }

    [TestMethod]
    public async Task DefaultContextDisposalUnrootsCollectibleCodeWhileTheContextRemainsReachable()
    {
        WarpRuntimeModule module = RuntimeLifecycleTests.LoadModule();
        var context = new WarpRuntimeContext(module, WarpBackendKind.CoreCLR);
        await using var contextLease = context.ConfigureAwait(false);
        await context.DispatchIntegerMapAsync(
            ManifestAssemblyFixture.MapEntryIdentity, [new uint[] { 3 }], [7]).ConfigureAwait(false);
        WeakReference assembly = await WarpCacheLifetimeProbe.CaptureCompiledAssemblyAsync(context, module).ConfigureAwait(false);
        Assert.IsTrue(assembly.IsAlive);
        Assert.AreEqual(1, context.JitStatistics.MemoryEntryCount);

        await context.DisposeAsync().ConfigureAwait(false);

        Assert.AreEqual(0, context.JitStatistics.MemoryEntryCount);
        for (int attempt = 0; attempt < 20 && assembly.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (assembly.IsAlive)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);
            }
        }

        Assert.IsFalse(assembly.IsAlive, "A reachable disposed context must not retain its generated collectible assembly.");
        GC.KeepAlive(context);
    }

    [TestMethod]
    public async Task CallerSuppliedCacheSurvivesContextDisposalAndAnotherContextReusesItsCompilation()
    {
        WarpRuntimeModule module = RuntimeLifecycleTests.LoadModule();
        var cache = new WarpJitCache();
        await using var cacheLease = cache.ConfigureAwait(false);
        var first = new WarpRuntimeContext(module, WarpBackendKind.CoreCLR, jitCache: cache);
        await using var firstLease = first.ConfigureAwait(false);
        await first.DispatchIntegerMapAsync(
            ManifestAssemblyFixture.MapEntryIdentity, [new uint[] { 3 }], [7]).ConfigureAwait(false);
        Assert.AreEqual(1, cache.Statistics.MemoryEntryCount);
        Assert.AreEqual(1L, cache.Statistics.CompilationCount);

        await first.DisposeAsync().ConfigureAwait(false);

        Assert.AreEqual(WarpRuntimeContextState.Disposed, first.State);
        Assert.AreEqual(1, first.JitStatistics.MemoryEntryCount);
        var second = new WarpRuntimeContext(module, WarpBackendKind.CoreCLR, jitCache: cache);
        await using var secondLease = second.ConfigureAwait(false);
        uint[] output = await second.DispatchIntegerMapAsync(
            ManifestAssemblyFixture.MapEntryIdentity, [new uint[] { 5 }], [7]).ConfigureAwait(false);
        Assert.AreEqual(TestKernels.ManifestMap(5, 7), output[0]);
        Assert.AreEqual(1L, cache.Statistics.CompilationCount);
        Assert.AreEqual(1L, cache.Statistics.MemoryHitCount);

        await second.DisposeAsync().ConfigureAwait(false);

        Assert.AreEqual(1, cache.Statistics.MemoryEntryCount);
        Assert.AreEqual(1L, cache.Statistics.CompilationCount);
        GC.KeepAlive(first);
        GC.KeepAlive(cache);
    }

    [TestMethod]
    public async Task DiskCacheEnforcesItsBudgetAndDoesNotDeleteUnrelatedFiles()
    {
        string directory = CreateDirectory();
        try
        {
            string sentinel = Path.Combine(directory, "caller.txt");
            await File.WriteAllTextAsync(sentinel, "keep").ConfigureAwait(false);
            var cache = new WarpJitCache(new WarpJitCacheOptions { DirectoryPath = directory, MaximumDiskBytes = 1 });
            await using var cacheLease = cache.ConfigureAwait(false);
            var context = new WarpRuntimeContext(RuntimeLifecycleTests.LoadModule(), WarpBackendKind.CoreCLR, jitCache: cache);
            await using var contextLease = context.ConfigureAwait(false);
            await context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [new uint[] { 1 }], [0]).ConfigureAwait(false);
            Assert.IsEmpty(Directory.GetFiles(directory, "*.wrcache"));
            Assert.AreEqual("keep", await File.ReadAllTextAsync(sentinel).ConfigureAwait(false), StringComparer.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void InvalidCacheResourcesAreRejectedBeforeFilesystemMutation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WarpJitCache(new WarpJitCacheOptions { MaximumMemoryEntries = 0 }));
        Assert.Throws<ArgumentException>(() => new WarpJitCache(new WarpJitCacheOptions { DirectoryPath = "relative/cache" }));
    }

    private static string CreateDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "warpclr-jit-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
