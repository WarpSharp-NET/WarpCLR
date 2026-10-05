using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Tests.Production;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLRWorkerTests
{
    [TestMethod]
    public async Task CancelledCompilationWaiterDoesNotCancelSingleFlightNativeWorker()
    {
        var cache = new WarpJitCache();
        await using var cacheOwner = cache.ConfigureAwait(false);
        WarpRuntimeModule module = RuntimeLifecycleTests.LoadModule();
        WarpRuntimeEntry entry = module.Entries[ManifestAssemblyFixture.MapEntryIdentity];
        using var cancellation = new CancellationTokenSource();
        Task<WarpCoreCLRWorkerLease> cancelled = cache.GetOrCompileAsync(module, entry, cancellation.Token);
        Task<WarpCoreCLRWorkerLease> surviving = cache.GetOrCompileAsync(module, entry, CancellationToken.None);
        await cancellation.CancelAsync().ConfigureAwait(false);
        try
        {
            WarpCoreCLRWorkerLease unexpected = await cancelled.ConfigureAwait(false);
            await using var unexpectedOwner = unexpected.ConfigureAwait(false);
            Assert.Fail("Cancelled waiter received a module lease.");
        }
        catch (OperationCanceledException) { }
        WarpCoreCLRWorkerLease lease = await surviving.ConfigureAwait(false);
        await using var leaseOwner = lease.ConfigureAwait(false);
        WarpCoreCLRWorkerLease reused = await cache.GetOrCompileAsync(module, entry, CancellationToken.None).ConfigureAwait(false);
        await using var reusedOwner = reused.ConfigureAwait(false);
        Assert.AreEqual(lease.ProcessId, reused.ProcessId); Assert.AreEqual(1L, cache.Statistics.CompilationCount);
        uint[] state = entry.Layout.CreateInitialState(32, 1000);
        while (state[0] == WarpLogicalMachineLayout.Runnable)
        { await reused.ExecuteManagedQuantumAsync([[3]], [7], 0, state, 32, 4096, [], CancellationToken.None).ConfigureAwait(false); }
        Assert.AreEqual(106u, state[WarpLogicalMachineLayout.ResultOffset]);
    }

    [TestMethod]
    public async Task EvictionKeepsAnActiveLeaseAliveAndAdmissionCountsRetiredNativeProcesses()
    {
        var cache = new WarpJitCache(new() { MaximumMemoryEntries = 1, CoreCLR = new() { MaximumWorkerProcesses = 2 } });
        await using var cacheOwner = cache.ConfigureAwait(false);
        WarpRuntimeModule module = RuntimeLifecycleTests.LoadModule();
        WarpRuntimeEntry map = module.Entries[ManifestAssemblyFixture.MapEntryIdentity];
        WarpCoreCLRWorkerLease first = await cache.GetOrCompileAsync(module, map, CancellationToken.None).ConfigureAwait(false);
        await using var firstOwner = first.ConfigureAwait(false);
        WarpCoreCLRWorkerLease second = await cache.GetOrCompileAsync(module,
            module.Entries[ManifestAssemblyFixture.ReductionEntryIdentity], CancellationToken.None).ConfigureAwait(false);
        await using var secondOwner = second.ConfigureAwait(false);
        Assert.IsFalse(first.IsFaulted);
        uint[] state = map.Layout.CreateInitialState(32, 1000);
        while (state[0] == WarpLogicalMachineLayout.Runnable)
        { await first.ExecuteManagedQuantumAsync([[3]], [7], 0, state, 32, 4096, [], CancellationToken.None).ConfigureAwait(false); }
        Assert.AreEqual(106u, state[WarpLogicalMachineLayout.ResultOffset]);
        WarpHostException rejected = await Assert.ThrowsAsync<WarpHostException>(() =>
            cache.GetOrCompileAsync(module, map, CancellationToken.None)).ConfigureAwait(false);
        Assert.AreEqual("WRPCORECLR3004", rejected.Code, StringComparer.Ordinal);
        int retired = first.ProcessId;
        await first.DisposeAsync().ConfigureAwait(false);
        AssertTerminated(retired);
    }
}
