using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Runtime.Host.Native;

namespace WarpCLR.Tests.Architecture;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest creates this internal fixture through reflected discovery.")]
internal sealed class NativeCachePolicyTests
{
    [TestMethod]
    public async Task SharedNativeCompilationSurvivesOneWaiterCancellation()
    {
        WarpRuntimeModule module = LoadModule();
        WarpRuntimeEntry entry = module.Entries[ManifestAssemblyFixture.MapEntryIdentity];
        var cache = new WarpJitCache();
        await using var cacheLease = cache.ConfigureAwait(false);
        WarpNativeTarget target = Target();
        using var started = new SemaphoreSlim(0, 1);
        using var release = new SemaphoreSlim(0, 1);
        using var cancellation = new CancellationTokenSource();
        int compilations = 0;
        async Task<WarpNativeImage> CompileAsync(CancellationToken token)
        {
            Assert.IsFalse(token.IsCancellationRequested);
            Interlocked.Increment(ref compilations);
            started.Release();
            await release.WaitAsync(token).ConfigureAwait(false);
            Assert.IsFalse(token.IsCancellationRequested);
            return Image(entry.Layout, target);
        }

        Task<WarpNativeImage> first = cache.GetOrCompileNativeAsync(module, entry, target, "toolchain-v1", CompileAsync, cancellation.Token);
        Assert.IsTrue(await started.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false));
        Task<WarpNativeImage> second = cache.GetOrCompileNativeAsync(module, entry, target, "toolchain-v1", CompileAsync, CancellationToken.None);
        await cancellation.CancelAsync().ConfigureAwait(false);
        try { await first.ConfigureAwait(false); Assert.Fail("The canceled waiter published a native image."); }
        catch (OperationCanceledException) { Assert.IsTrue(first.IsCanceled); }
        release.Release();
        WarpNativeImage image = await second.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        Assert.AreEqual(WarpConformanceStatus.DevelopmentNonconforming, image.ConformanceStatus);
        Assert.AreEqual(1, compilations);
        Assert.AreEqual(1, cache.Statistics.CompilationCount);
        Assert.AreEqual(1, cache.Statistics.MemoryHitCount);
        Assert.AreEqual(1, cache.Statistics.MemoryEntryCount);
    }

    [TestMethod]
    public async Task OwnedCacheShutdownCancelsItsCompilationAndClearsCodeReferences()
    {
        WarpRuntimeModule module = LoadModule();
        WarpRuntimeEntry entry = module.Entries[ManifestAssemblyFixture.MapEntryIdentity];
        var cache = new WarpJitCache();
        await using var cacheLease = cache.ConfigureAwait(false);
        WarpNativeTarget target = Target();
        using var started = new SemaphoreSlim(0, 1);
        using var cancelled = new SemaphoreSlim(0, 1);
        async Task<WarpNativeImage> CompileAsync(CancellationToken token)
        {
            started.Release();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { cancelled.Release(); throw; }
            return Image(entry.Layout, target);
        }

        Task<WarpNativeImage> request = cache.GetOrCompileNativeAsync(module, entry, target, "toolchain-v1", CompileAsync, CancellationToken.None);
        Assert.IsTrue(await started.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false));
        Task[] shutdowns = Enumerable.Range(0, 8).Select(_ => cache.DisposeAsync().AsTask()).ToArray();
        await Task.WhenAll(shutdowns).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        Assert.IsTrue(await cancelled.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false));
        try { await request.ConfigureAwait(false); Assert.Fail("The owned shutdown did not cancel its native compilation."); }
        catch (OperationCanceledException) { Assert.IsTrue(request.IsCanceled); }
        Assert.AreEqual(0, cache.Statistics.MemoryEntryCount);
        Assert.AreEqual(0, cache.Statistics.CompilationCount);
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => GetImageAsync(cache, module, entry, target)).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task OwnedCacheShutdownAlsoDropsCompletedNativeImages()
    {
        WarpRuntimeModule module = LoadModule();
        WarpRuntimeEntry entry = module.Entries[ManifestAssemblyFixture.MapEntryIdentity];
        var cache = new WarpJitCache();
        await using var cacheLease = cache.ConfigureAwait(false);
        await GetImageAsync(cache, module, entry, Target()).ConfigureAwait(false);
        Assert.AreEqual(1, cache.Statistics.MemoryEntryCount);
        await cache.DisposeAsync().ConfigureAwait(false);
        Assert.AreEqual(0, cache.Statistics.MemoryEntryCount);
        Assert.AreEqual(1, cache.Statistics.CompilationCount);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public async Task OwnedCacheShutdownDrainsAdmissionFailuresButDoesNotHideCompilerBugs(int failureKind)
    {
        WarpRuntimeModule module = LoadModule();
        WarpRuntimeEntry entry = module.Entries[ManifestAssemblyFixture.MapEntryIdentity];
        var cache = new WarpJitCache();
        using var started = new SemaphoreSlim(0, 1);
        using var release = new SemaphoreSlim(0, 1);
        async Task<WarpNativeImage> CompileAsync(CancellationToken _)
        {
            started.Release();
            await release.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            Exception fault = failureKind switch
            {
                0 => new WarpCompilationResourceException("fixture", WarpCompilationResourceKind.SourceBytes, 2, 1),
                1 => new CoreCLRCompilationResourceException("fixture", 2, 1),
                _ => new InvalidOperationException("An unexpected compiler programming fault must escape shutdown."),
            };
            throw fault;
        }

        Task<WarpNativeImage> request = cache.GetOrCompileNativeAsync(module, entry, Target(), "toolchain-v1", CompileAsync, CancellationToken.None);
        Assert.IsTrue(await started.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false));
        Task shutdown = cache.DisposeAsync().AsTask();
        release.Release();
        Type expected = failureKind switch
        {
            0 => typeof(WarpCompilationResourceException),
            1 => typeof(CoreCLRCompilationResourceException),
            _ => typeof(InvalidOperationException),
        };
        try
        {
            await Task.WhenAll(shutdown, request).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.Fail("The fixture compilation fault was hidden.");
        }
        catch (Exception error) when (error is WarpCompilationResourceException or CoreCLRCompilationResourceException or InvalidOperationException)
        { Assert.AreEqual(expected, error.GetType()); }
        Assert.IsTrue(request.IsFaulted);
        Assert.AreEqual(expected, request.Exception!.GetBaseException().GetType());
        Assert.AreEqual(failureKind == 2, shutdown.IsFaulted);
        Assert.AreEqual(failureKind != 2, shutdown.IsCompletedSuccessfully);
        Assert.AreEqual(0, cache.Statistics.MemoryEntryCount);
        Assert.AreEqual(0, cache.Statistics.CompilationCount);
    }

    [TestMethod]
    public async Task CoreClrAndNativeCompilationsShareTheSameAdmissionBudget()
    {
        WarpRuntimeModule module = LoadModule();
        WarpRuntimeEntry entry = module.Entries[ManifestAssemblyFixture.MapEntryIdentity];
        var cache = new WarpJitCache(new WarpJitCacheOptions { MaximumConcurrentCompilations = 1, MaximumMemoryEntries = 2 });
        await using var cacheLease = cache.ConfigureAwait(false);
        WarpNativeTarget target = Target();
        using var started = new SemaphoreSlim(0, 1);
        using var release = new SemaphoreSlim(0, 1);
        async Task<WarpNativeImage> CompileAsync(CancellationToken token)
        {
            Assert.IsFalse(token.IsCancellationRequested);
            started.Release();
            await release.WaitAsync(token).ConfigureAwait(false);
            return Image(entry.Layout, target);
        }

        Task<WarpNativeImage> native = cache.GetOrCompileNativeAsync(module, entry, target, "toolchain-v1", CompileAsync, CancellationToken.None);
        Assert.IsTrue(await started.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false));
        WarpHostException admission = await Assert.ThrowsAsync<WarpHostException>(() =>
            cache.GetOrCompileAsync(module, entry, CancellationToken.None)).ConfigureAwait(false);
        Assert.AreEqual("WRPRUNTIME1002", admission.Code, StringComparer.Ordinal);
        release.Release();
        await native.ConfigureAwait(false);
        await cache.GetOrCompileAsync(module, entry, CancellationToken.None).ConfigureAwait(false);
        Assert.AreEqual(2, cache.Statistics.CompilationCount);
        Assert.AreEqual(2, cache.Statistics.MemoryEntryCount);
    }

    [TestMethod]
    public async Task NativeCacheKeysBindPhysicalTargetToolchainClosureAndAuthorizedAssembly()
    {
        WarpRuntimeModule module = LoadModule();
        WarpRuntimeEntry map = module.Entries[ManifestAssemblyFixture.MapEntryIdentity];
        WarpRuntimeEntry reduction = module.Entries[ManifestAssemblyFixture.ReductionEntryIdentity];
        var cache = new WarpJitCache(new WarpJitCacheOptions { MaximumMemoryEntries = 16 });
        await using var cacheLease = cache.ConfigureAwait(false);
        WarpNativeTarget target = Target();
        await GetImageAsync(cache, module, map, target).ConfigureAwait(false);
        await GetImageAsync(cache, module, map, target).ConfigureAwait(false);
        await GetImageAsync(cache, module, map, Target("sm_86")).ConfigureAwait(false);
        await GetImageAsync(cache, module, map, Target(runtime: "driver-v2")).ConfigureAwait(false);
        await GetImageAsync(cache, module, map, Target(device: "physical-device-other")).ConfigureAwait(false);
        await GetImageAsync(cache, module, map, target, "toolchain-v2").ConfigureAwait(false);
        await GetImageAsync(cache, module, reduction, target).ConfigureAwait(false);
        byte[] changed = ManifestAssemblyFixture.ReadAssembly();
        changed[0x40] ^= 1; // DOS stub bytes are outside CLR code/metadata but remain part of the authorized whole-assembly hash.
        WarpRuntimeModule authorizedChanged = LoadModule(changed);
        Assert.AreNotEqual(module.AssemblyHash, authorizedChanged.AssemblyHash, StringComparer.Ordinal);
        Assert.AreEqual(module.ManifestHash, authorizedChanged.ManifestHash, StringComparer.Ordinal);
        await GetImageAsync(cache, authorizedChanged, authorizedChanged.Entries[map.Identity], target).ConfigureAwait(false);
        Assert.AreEqual(7, cache.Statistics.CompilationCount);
        Assert.AreEqual(1, cache.Statistics.MemoryHitCount);
        Assert.AreEqual(7, cache.Statistics.MemoryEntryCount);
    }

    [TestMethod]
    public async Task NativeCacheRejectsChangedCompilationIdentityAndAllowsFreshRetry()
    {
        WarpRuntimeModule module = LoadModule();
        WarpRuntimeEntry entry = module.Entries[ManifestAssemblyFixture.MapEntryIdentity];
        WarpNativeTarget target = Target();
        var cache = new WarpJitCache();
        await using var cacheLease = cache.ConfigureAwait(false);
        WarpHostException changed = await Assert.ThrowsAsync<WarpHostException>(() => cache.GetOrCompileNativeAsync(module,
            entry, target, "toolchain-v1", _ => Task.FromResult(Image(entry.Layout, target, "toolchain-v2")), CancellationToken.None)).ConfigureAwait(false);
        Assert.AreEqual("WRPNATIVE2006", changed.Code, StringComparer.Ordinal);
        Assert.AreEqual(0, cache.Statistics.CompilationCount);
        Assert.AreEqual(0, cache.Statistics.MemoryEntryCount);
        await GetImageAsync(cache, module, entry, target, "toolchain-v2").ConfigureAwait(false);
        Assert.AreEqual(1, cache.Statistics.CompilationCount);
    }

    [TestMethod]
    public async Task NativeDiskCacheNeverExecutesTamperedBytesAndEvictsOnlyOwnedFiles()
    {
        WarpRuntimeModule module = LoadModule();
        WarpRuntimeEntry entry = module.Entries[ManifestAssemblyFixture.MapEntryIdentity];
        byte[] canonical = WarpCoreCLRPlanCodec.Serialize(entry.Kernel);
        string directory = Directory.CreateTempSubdirectory("warpclr-native-cache-policy-").FullName;
        try
        {
            var options = new WarpJitCacheOptions { DirectoryPath = directory, MaximumDiskBytes = canonical.Length };
            var first = new WarpJitCache(options);
            await using var firstLease = first.ConfigureAwait(false);
            await GetImageAsync(first, module, entry, Target()).ConfigureAwait(false);
            string plan = Directory.GetFiles(directory, "*.wrcache").SingleItem(static _ => true);
            await File.WriteAllBytesAsync(plan, Encoding.UTF8.GetBytes("tampered executable payload; not an execution authority")).ConfigureAwait(false);
            var second = new WarpJitCache(options);
            await using var secondLease = second.ConfigureAwait(false);
            await GetImageAsync(second, module, entry, Target()).ConfigureAwait(false);
            Assert.AreEqual(0, second.Statistics.DiskHitCount);
            Assert.AreEqual(1, second.Statistics.CompilationCount);
            CollectionAssert.AreEqual(canonical, await File.ReadAllBytesAsync(plan).ConfigureAwait(false));
            var third = new WarpJitCache(options);
            await using var thirdLease = third.ConfigureAwait(false);
            await GetImageAsync(third, module, entry, Target()).ConfigureAwait(false);
            Assert.AreEqual(1, third.Statistics.DiskHitCount);
            Assert.AreEqual(1, third.Statistics.CompilationCount);
            string callerCache = Path.Combine(directory, "caller.wrcache");
            string callerText = Path.Combine(directory, "caller.txt");
            await File.WriteAllTextAsync(callerCache, "caller-owned-cache").ConfigureAwait(false);
            await File.WriteAllTextAsync(callerText, "caller-owned-text").ConfigureAwait(false);
            await GetImageAsync(third, module, entry, Target(), "toolchain-v2").ConfigureAwait(false);
            Assert.AreEqual("caller-owned-cache", await File.ReadAllTextAsync(callerCache).ConfigureAwait(false), StringComparer.Ordinal);
            Assert.AreEqual("caller-owned-text", await File.ReadAllTextAsync(callerText).ConfigureAwait(false), StringComparer.Ordinal);
            Assert.HasCount(2, Directory.GetFiles(directory, "*.wrcache"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task OptionalDiskPersistenceStopsBeforeDeletingAnOverBudgetDirectory()
    {
        WarpRuntimeModule module = LoadModule();
        WarpRuntimeEntry entry = module.Entries[ManifestAssemblyFixture.MapEntryIdentity];
        string directory = Directory.CreateTempSubdirectory("warpclr-native-cache-scan-").FullName;
        try
        {
            string[] reservedNames = Enumerable.Range(0, 6).Select(index => new string((char)('A' + index), 64) + ".wrcache").ToArray();
            foreach (string name in reservedNames)
            {
                await File.WriteAllBytesAsync(Path.Combine(directory, name), Array.Empty<byte>()).ConfigureAwait(false);
            }

            string caller = Path.Combine(directory, "caller.wrcache");
            await File.WriteAllTextAsync(caller, "unrelated caller content").ConfigureAwait(false);
            var cache = new WarpJitCache(new WarpJitCacheOptions { DirectoryPath = directory, MaximumDiskEntries = 4 });
            await using var lease = cache.ConfigureAwait(false);
            await GetImageAsync(cache, module, entry, Target()).ConfigureAwait(false);
            Assert.AreEqual(1, cache.Statistics.CompilationCount);
            Assert.AreEqual(1, cache.Statistics.MemoryEntryCount);
            foreach (string name in reservedNames) { Assert.IsTrue(File.Exists(Path.Combine(directory, name))); }
            Assert.AreEqual("unrelated caller content", await File.ReadAllTextAsync(caller).ConfigureAwait(false), StringComparer.Ordinal);
            Assert.HasCount(7, Directory.GetFiles(directory, "*.wrcache"));
            Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task BoundedDiskIndexEvictsOnlyOldReservedEntriesAtCapacity()
    {
        WarpRuntimeModule module = LoadModule();
        WarpRuntimeEntry entry = module.Entries[ManifestAssemblyFixture.MapEntryIdentity];
        string directory = Directory.CreateTempSubdirectory("warpclr-native-cache-index-").FullName;
        try
        {
            string first = Path.Combine(directory, new string('A', 64) + ".wrcache");
            string second = Path.Combine(directory, new string('B', 64) + ".wrcache");
            string caller = Path.Combine(directory, "caller.wrcache");
            await File.WriteAllBytesAsync(first, Array.Empty<byte>()).ConfigureAwait(false);
            await File.WriteAllBytesAsync(second, Array.Empty<byte>()).ConfigureAwait(false);
            await File.WriteAllTextAsync(caller, "caller data").ConfigureAwait(false);
            File.SetLastWriteTimeUtc(first, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(second, new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc));
            var cache = new WarpJitCache(new WarpJitCacheOptions { DirectoryPath = directory, MaximumDiskEntries = 4 });
            await using var lease = cache.ConfigureAwait(false);
            await GetImageAsync(cache, module, entry, Target()).ConfigureAwait(false);
            Assert.IsFalse(File.Exists(first));
            Assert.IsTrue(File.Exists(second));
            Assert.AreEqual("caller data", await File.ReadAllTextAsync(caller).ConfigureAwait(false), StringComparer.Ordinal);
            Assert.HasCount(4, Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(1)]
    public async Task OversizedOrTruncatedDiskSnapshotsCannotCountAsCanonicalCompilationInputs(int sizeDelta)
    {
        WarpRuntimeModule module = LoadModule();
        WarpRuntimeEntry entry = module.Entries[ManifestAssemblyFixture.MapEntryIdentity];
        byte[] canonical = WarpCoreCLRPlanCodec.Serialize(entry.Kernel);
        string directory = Directory.CreateTempSubdirectory("warpclr-native-cache-snapshot-").FullName;
        try
        {
            var options = new WarpJitCacheOptions { DirectoryPath = directory };
            var first = new WarpJitCache(options);
            await using var firstLease = first.ConfigureAwait(false);
            await GetImageAsync(first, module, entry, Target()).ConfigureAwait(false);
            string path = Directory.GetFiles(directory, "*.wrcache").SingleItem(static _ => true);
            byte[] changed = new byte[canonical.Length + sizeDelta];
            canonical.AsSpan(0, Math.Min(changed.Length, canonical.Length)).CopyTo(changed);
            await File.WriteAllBytesAsync(path, changed).ConfigureAwait(false);
            var second = new WarpJitCache(options);
            await using var secondLease = second.ConfigureAwait(false);
            await GetImageAsync(second, module, entry, Target()).ConfigureAwait(false);
            Assert.AreEqual(0, second.Statistics.DiskHitCount);
            Assert.AreEqual(1, second.Statistics.CompilationCount);
            CollectionAssert.AreEqual(canonical, await File.ReadAllBytesAsync(path).ConfigureAwait(false));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(65537)]
    public void DiskEntryLimitsRejectInvalidOptionsBeforeDirectoryCreation(int limit)
    {
        string missing = Path.Combine(Path.GetTempPath(), "warpclr-cache-invalid-" + Guid.NewGuid().ToString("N"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new WarpJitCache(new WarpJitCacheOptions
        {
            DirectoryPath = missing,
            MaximumDiskEntries = limit,
        }));
        Assert.IsFalse(Directory.Exists(missing));
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(1)]
    public void CanonicalSnapshotReadRejectsChangesAfterItsReportedLengthCheck(int sizeDelta)
    {
        WarpRuntimeModule module = LoadModule();
        byte[] canonical = WarpCoreCLRPlanCodec.Serialize(module.Entries[ManifestAssemblyFixture.MapEntryIdentity].Kernel);
        byte[] changed = new byte[canonical.Length + sizeDelta];
        canonical.AsSpan(0, Math.Min(changed.Length, canonical.Length)).CopyTo(changed);
        using var stream = new ReportedLengthStream(changed, canonical.LongLength);
        Assert.IsFalse(WarpJitCache.MatchesCanonicalSnapshot(stream, canonical));
    }

    [TestMethod]
    public async Task NativeProviderSharesOneModuleAndReportsCommonCacheStatistics()
    {
        WarpRuntimeModule module = LoadModule();
        WarpRuntimeEntry entry = module.Entries[ManifestAssemblyFixture.MapEntryIdentity];
        var cache = new WarpJitCache();
        await using var cacheLease = cache.ConfigureAwait(false);
        using var driver = new DriverFixture(Target());
        var provider = Provider(module, cache, driver);
        await using var lease = provider.ConfigureAwait(false);
        Task<IWarpNativeModule>[] requests = Enumerable.Range(0, 32).Select(_ => provider.GetOrCompileAsync(entry, CancellationToken.None)).ToArray();
        IWarpNativeModule[] loaded = await Task.WhenAll(requests).ConfigureAwait(false);
        foreach (IWarpNativeModule item in loaded) { Assert.AreSame(loaded[0], item); }
        Assert.AreEqual(1, driver.Loads);
        Assert.AreEqual(1, cache.Statistics.CompilationCount);
        Assert.AreEqual(31, cache.Statistics.MemoryHitCount);
        Task[] disposals = Enumerable.Range(0, 8).Select(_ => provider.DisposeAsync().AsTask()).ToArray();
        await Task.WhenAll(disposals).ConfigureAwait(false);
        Assert.AreEqual(1, driver.Disposals);
        Assert.IsTrue(driver.Modules.SingleItem(static _ => true).Disposed);
    }

    [TestMethod]
    public async Task NativeProviderQueuedLoadAfterShutdownCancelsWithoutOpeningDriver()
    {
        WarpRuntimeModule module = LoadModule();
        using var driver = new DriverFixture(Target());
        var cache = new WarpJitCache();
        await using var cacheLease = cache.ConfigureAwait(false);
        var provider = Provider(module, cache, driver);
        await using var lease = provider.ConfigureAwait(false);
        var gate = (Lock)typeof(WarpNativeExecutionProvider).GetField("sync", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(provider)!;
        Task<IWarpNativeModule> request;
        Task disposal;
        using (gate.EnterScope())
        {
            request = provider.GetOrCompileAsync(module.Entries[ManifestAssemblyFixture.MapEntryIdentity], CancellationToken.None);
            disposal = provider.DisposeAsync().AsTask();
        }

        await disposal.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        try { await request.ConfigureAwait(false); Assert.Fail("The queued native load ignored shutdown."); }
        catch (OperationCanceledException) { Assert.IsTrue(request.IsCanceled); }
        Assert.AreEqual(0, driver.Opens);
        Assert.AreEqual(0, driver.Loads);
    }

    [TestMethod]
    public async Task NativeProviderShutdownDoesNotCancelAnImageSharedByAnotherContext()
    {
        WarpRuntimeModule module = LoadModule();
        WarpRuntimeEntry entry = module.Entries[ManifestAssemblyFixture.MapEntryIdentity];
        var cache = new WarpJitCache();
        await using var cacheLease = cache.ConfigureAwait(false);
        using var firstDriver = new DriverFixture(Target());
        using var secondDriver = new DriverFixture(Target());
        using var started = new SemaphoreSlim(0, 1);
        using var release = new SemaphoreSlim(0, 1);
        int compilations = 0;
        async Task<WarpNativeImage> CompileAsync(WarpLogicalMachineLayout layout, WarpNativeTarget target, CancellationToken token)
        {
            Assert.IsFalse(token.IsCancellationRequested);
            Interlocked.Increment(ref compilations);
            started.Release();
            await release.WaitAsync(token).ConfigureAwait(false);
            Assert.IsFalse(token.IsCancellationRequested);
            return Image(layout, target);
        }

        var first = Provider(module, cache, firstDriver, compile: CompileAsync);
        var second = Provider(module, cache, secondDriver, compile: CompileAsync);
        await using var firstLease = first.ConfigureAwait(false);
        await using var secondLease = second.ConfigureAwait(false);
        Task<IWarpNativeModule> cancelled = first.GetOrCompileAsync(entry, CancellationToken.None);
        Assert.IsTrue(await started.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false));
        await first.DisposeAsync().ConfigureAwait(false);
        try { await cancelled.ConfigureAwait(false); Assert.Fail("The disposed context published a native module."); }
        catch (OperationCanceledException) { Assert.IsTrue(cancelled.IsCanceled); }
        Task<IWarpNativeModule> survivor = second.GetOrCompileAsync(entry, CancellationToken.None);
        release.Release();
        await survivor.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        Assert.AreEqual(1, compilations);
        Assert.AreEqual(1, firstDriver.Disposals);
        Assert.AreEqual(0, firstDriver.Loads);
        Assert.AreEqual(1, secondDriver.Loads);
        Assert.AreEqual(1, cache.Statistics.CompilationCount);
    }

    [TestMethod]
    public async Task NativeProviderRetriesAFailedToolchainProbe()
    {
        WarpRuntimeModule module = LoadModule();
        WarpRuntimeEntry entry = module.Entries[ManifestAssemblyFixture.MapEntryIdentity];
        using var driver = new DriverFixture(Target());
        int probes = 0;
        Task<string> IdentifyAsync(WarpNativeTarget _, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref probes) == 1) { throw new WarpHostException("WRPNATIVE2001", "Fixture compiler initially absent."); }
            return Task.FromResult("toolchain-v1");
        }

        var cache = new WarpJitCache();
        await using var cacheLease = cache.ConfigureAwait(false);
        var provider = Provider(module, cache, driver, identify: IdentifyAsync);
        await using var lease = provider.ConfigureAwait(false);
        await Assert.ThrowsAsync<WarpHostException>(() => provider.GetOrCompileAsync(entry, CancellationToken.None)).ConfigureAwait(false);
        await provider.GetOrCompileAsync(entry, CancellationToken.None).ConfigureAwait(false);
        Assert.AreEqual(2, probes);
        Assert.AreEqual(1, driver.Loads);
    }

    [TestMethod]
    public async Task FailedModuleAfterItsSoleWaiterCancelsCannotConsumeResidentAdmission()
    {
        WarpRuntimeModule module = LoadModule();
        WarpRuntimeEntry firstEntry = module.Entries[ManifestAssemblyFixture.MapEntryIdentity];
        WarpRuntimeEntry nextEntry = module.Entries[ManifestAssemblyFixture.ReductionEntryIdentity];
        var cache = new WarpJitCache();
        await using var cacheLease = cache.ConfigureAwait(false);
        using var driver = new DriverFixture(Target());
        using var started = new SemaphoreSlim(0, 1);
        using var release = new SemaphoreSlim(0, 1);
        using var cancellation = new CancellationTokenSource();
        int attempts = 0;
        async Task<WarpNativeImage> CompileAsync(WarpLogicalMachineLayout layout, WarpNativeTarget target, CancellationToken token)
        {
            Assert.IsFalse(token.IsCancellationRequested);
            if (Interlocked.Increment(ref attempts) == 1)
            {
                started.Release();
                await release.WaitAsync(token).ConfigureAwait(false);
                throw new WarpHostException("WRPNATIVE2002", "Fixture native compiler failed after waiter cancellation.");
            }

            return Image(layout, target);
        }

        var provider = Provider(module, cache, driver, new WarpNativeRuntimeOptions { MaximumCachedModules = 1 }, compile: CompileAsync);
        await using var lease = provider.ConfigureAwait(false);
        Task<IWarpNativeModule> caller = provider.GetOrCompileAsync(firstEntry, cancellation.Token);
        Task<IWarpNativeModule> pending = PendingModuleAsync(provider);
        Assert.IsTrue(await started.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false));
        await cancellation.CancelAsync().ConfigureAwait(false);
        try { await caller.ConfigureAwait(false); Assert.Fail("The canceled sole waiter published a native module."); }
        catch (OperationCanceledException) { Assert.IsTrue(caller.IsCanceled); }
        release.Release();
        try { await pending.ConfigureAwait(false); Assert.Fail("The fixture native compiler failure was hidden."); }
        catch (WarpHostException error) { Assert.AreEqual("WRPNATIVE2002", error.Code, StringComparer.Ordinal); }
        await provider.GetOrCompileAsync(nextEntry, CancellationToken.None).ConfigureAwait(false);
        Assert.AreEqual(2, attempts);
        Assert.AreEqual(1, driver.Loads);
        Assert.AreEqual(1, cache.Statistics.MemoryEntryCount);
    }

    private static WarpRuntimeModule LoadModule(byte[]? bytes = null)
    {
        byte[] owned = bytes ?? ManifestAssemblyFixture.ReadAssembly();
        return WarpRuntimeModule.Load(owned, new WarpModuleTrust([Convert.ToHexString(SHA256.HashData(owned))]));
    }

    private static Task<WarpNativeImage> GetImageAsync(WarpJitCache cache, WarpRuntimeModule module, WarpRuntimeEntry entry,
        WarpNativeTarget target, string identity = "toolchain-v1") => cache.GetOrCompileNativeAsync(module, entry, target,
            identity, _ => Task.FromResult(Image(entry.Layout, target, identity)), CancellationToken.None);

    private static WarpNativeTarget Target(string architecture = "sm_80", string device = "physical-device", string runtime = "driver-v1") =>
        new(WarpBackendKind.NVPTX, architecture, device, runtime, 256, uint.MaxValue, 1UL << 32);

    private static WarpNativeImage Image(WarpLogicalMachineLayout layout, WarpNativeTarget target, string identity = "toolchain-v1") =>
        new(target, WarpNativeImageFormat.Ptx, WarpPortableMachineEmitter.EntryPoint, [1], "fixture-source", identity,
            layout.Kernel.InputBufferCount, layout.Kernel.ScalarArgumentCount, layout);

    private static WarpNativeExecutionProvider Provider(WarpRuntimeModule module, WarpJitCache cache, DriverFixture driver,
        WarpNativeRuntimeOptions? options = null,
        Func<WarpNativeTarget, CancellationToken, Task<string>>? identify = null,
        Func<WarpLogicalMachineLayout, WarpNativeTarget, CancellationToken, Task<WarpNativeImage>>? compile = null) =>
        new(module, cache, options ?? new WarpNativeRuntimeOptions(), driver.Open,
            identify ?? ((_, _) => Task.FromResult("toolchain-v1")), compile ?? ((layout, target, _) => Task.FromResult(Image(layout, target))));

    private static Task<IWarpNativeModule> PendingModuleAsync(WarpNativeExecutionProvider provider) =>
        ((Dictionary<string, Task<IWarpNativeModule>>)typeof(WarpNativeExecutionProvider)
            .GetField("modules", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(provider)!).SingleItem(static _ => true).Value;

    private sealed class ReportedLengthStream(byte[] bytes, long reportedLength) : MemoryStream(bytes, writable: false)
    {
        public override long Length => reportedLength;
    }

    // This metadata/lifetime fixture performs no driver calls or kernel execution. It is not native conformance proof.
    private sealed class DriverFixture(WarpNativeTarget target) : IWarpNativeDriver
    {
        public WarpNativeTarget Target { get; } = target;
        public List<ModuleFixture> Modules { get; } = [];
        public int Opens { get; private set; }
        public int Loads { get; private set; }
        public int Disposals { get; private set; }
        public DriverFixture Open() { Opens++; return this; }
        public IWarpNativeModule Load(WarpNativeImage image)
        {
            Loads++;
            var loaded = new ModuleFixture(image);
            Modules.Add(loaded);
            return loaded;
        }

        public void Dispose()
        {
            if (Disposals != 0) { return; }
            Disposals++;
            foreach (ref readonly ModuleFixture item in CollectionsMarshal.AsSpan(Modules)) { item.Dispose(); }
        }
    }

    private sealed class ModuleFixture(WarpNativeImage image) : IWarpNativeModule
    {
        public WarpNativeImage Image { get; } = image;
        public bool IsFaulted => false;
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
        public WarpNativeManagedArena CreateManagedArena(uint[] initial) => throw new NotSupportedException("The cache identity fixture does not allocate device memory.");

        public IWarpNativeMachineExecution CreateMachineExecution(uint[] states, IReadOnlyList<uint[]> inputs,
            IReadOnlyList<uint> scalars, int itemCount, int inputBase, int maximumCallDepth,
            WarpNativeManagedArena? managedArena = null) => throw NoExecution();
        public uint[] ResumeUInt32(uint[] states, IReadOnlyList<uint[]> inputs, IReadOnlyList<uint> scalars, int itemCount,
            int inputBase, int maximumCallDepth, int quantum, CancellationToken cancellationToken = default) => throw NoExecution();
        public uint ReduceUInt32(uint[] values, WarpReductionOperation operation, CancellationToken cancellationToken = default) => throw NoExecution();
        public uint[] DispatchUInt32(IReadOnlyList<uint[]> inputs, IReadOnlyList<uint> scalars, int itemCount, bool reduction,
            CancellationToken cancellationToken = default) => throw NoExecution();
        private static NotSupportedException NoExecution() => new("This lifecycle fixture does not execute any backend.");
    }
}
