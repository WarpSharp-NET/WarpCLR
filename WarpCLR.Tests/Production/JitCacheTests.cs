using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests.Production;

[TestClass]
public sealed class JitCacheTests
{
    [TestMethod]
    public async Task Corrupt_disk_cache_is_never_an_execution_authority_and_is_replaced_atomically()
    {
        string directory = CreateDirectory();
        try
        {
            WarpRuntimeModule module = RuntimeLifecycleTests.LoadModule();
            var options = new WarpJitCacheOptions { DirectoryPath = directory };
            await using (var first = new WarpRuntimeContext(module, WarpBackendKind.CoreCLR, jitCache: new WarpJitCache(options)))
            {
                uint[] output = await first.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [new uint[] { 3 }], [7]);
                Assert.AreEqual(106u, output[0]);
            }

            string path = Directory.GetFiles(directory, "*.wrcache").Single();
            byte[] original = File.ReadAllBytes(path);
            File.WriteAllBytes(path, [0xFF]);
            await using (var second = new WarpRuntimeContext(module, WarpBackendKind.CoreCLR, jitCache: new WarpJitCache(options)))
            {
                uint[] output = await second.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [new uint[] { 3 }], [7]);
                Assert.AreEqual(106u, output[0]);
                Assert.AreEqual(0, second.JitStatistics.DiskHitCount);
            }

            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            await using var third = new WarpRuntimeContext(module, WarpBackendKind.CoreCLR, jitCache: new WarpJitCache(options));
            await third.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [new uint[] { 3 }], [7]);
            Assert.AreEqual(1, third.JitStatistics.DiskHitCount);
            Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Memory_cache_eviction_releases_old_compilations_without_invalidating_dispatch()
    {
        var cache = new WarpJitCache(new WarpJitCacheOptions { MaximumMemoryEntries = 1 });
        await using var context = new WarpRuntimeContext(RuntimeLifecycleTests.LoadModule(), WarpBackendKind.CoreCLR, jitCache: cache);
        await context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [new uint[] { 1 }], [0]);
        await context.DispatchUInt32ReductionAsync(ManifestAssemblyFixture.ReductionEntryIdentity, [new uint[] { 1 }], [0]);
        await context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [new uint[] { 1 }], [0]);
        Assert.AreEqual(3, cache.Statistics.CompilationCount);
        Assert.AreEqual(1, cache.Statistics.MemoryEntryCount);
    }

    [TestMethod]
    public async Task Disk_cache_enforces_its_budget_and_does_not_delete_unrelated_files()
    {
        string directory = CreateDirectory();
        try
        {
            string sentinel = Path.Combine(directory, "caller.txt");
            File.WriteAllText(sentinel, "keep");
            var cache = new WarpJitCache(new WarpJitCacheOptions { DirectoryPath = directory, MaximumDiskBytes = 1 });
            await using var context = new WarpRuntimeContext(RuntimeLifecycleTests.LoadModule(), WarpBackendKind.CoreCLR, jitCache: cache);
            await context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [new uint[] { 1 }], [0]);
            Assert.IsEmpty(Directory.GetFiles(directory, "*.wrcache"));
            Assert.AreEqual("keep", File.ReadAllText(sentinel));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Invalid_cache_resources_are_rejected_before_filesystem_mutation()
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
