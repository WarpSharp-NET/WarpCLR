using System.Diagnostics.CodeAnalysis;
using System.Collections;
using System.Reflection;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Tests.Production;

namespace WarpCLR.Tests;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers and instantiates this fixture through reflection.")]
internal sealed class WarpRuntimeBoundaryTests
{
    [TestMethod]
    [DataRow(WarpRuntimeFaultKind.StepLimit)]
    [DataRow(WarpRuntimeFaultKind.CallDepth)]
    [DataRow(WarpRuntimeFaultKind.RuntimeFailure)]
    public async Task WorkerFaultQuarantinesImmediatelyEvenWhenAnotherWorkerIsCancelled(WarpRuntimeFaultKind kind)
    {
        WarpRuntimeModule module = RuntimeLifecycleTests.LoadModule();
        WarpRuntimeEntry entry = module.Entries[ManifestAssemblyFixture.MapEntryIdentity];
        var options = new WarpRuntimeOptions
        {
            MaximumStepsPerWorker = kind == WarpRuntimeFaultKind.StepLimit ? 1 : WarpRuntimeAbi.DefaultMaximumStepsPerWorker,
            MaximumCallDepth = kind == WarpRuntimeFaultKind.CallDepth ? 1 : WarpRuntimeAbi.DefaultMaximumCallDepth,
        };
        var context = new WarpRuntimeContext(module, WarpBackendKind.CoreCLR, options);
        await using var contextLease = context.ConfigureAwait(false);
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(entry.Layout);
        WorkerExecution executeWorker = BindWorker(context);
        uint[] state = entry.Layout.CreateInitialState(options.MaximumCallDepth, options.MaximumStepsPerWorker);
        uint[] unpublished = [0xDEADBEEFu];
        uint[][] inputs = kind == WarpRuntimeFaultKind.RuntimeFailure ? [] : [[3]];

        WarpRuntimeFaultException fault = executeWorker(entry, compiled, inputs, [7], 0, state, unpublished, CancellationToken.None)
            ?? throw new InvalidOperationException("The faulting compiled worker returned success.");

        Assert.AreEqual(kind, fault.Kind);
        Assert.AreEqual(0, fault.WorkerIndex);
        Assert.AreEqual(WarpRuntimeContextState.Faulted, context.State);
        Assert.AreEqual(0xDEADBEEFu, unpublished[0]);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(false);
        uint[] otherState = entry.Layout.CreateInitialState(options.MaximumCallDepth, options.MaximumStepsPerWorker);
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            executeWorker(entry, compiled, [[3]], [7], 0, otherState, unpublished, cancellation.Token));
        Assert.AreEqual(WarpRuntimeContextState.Faulted, context.State);
        WarpHostException rejected = await Assert.ThrowsAsync<WarpHostException>(() => context.DispatchIntegerMapAsync(
            entry.Identity, [Array.Empty<uint>()], [7])).ConfigureAwait(false);
        Assert.AreEqual("WRPRUNTIME1006", rejected.Code, StringComparer.Ordinal);
        Assert.AreEqual(0L, context.JitStatistics.CompilationCount);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AQueuedSuccessCannotPublishOrUndoQuarantineAfterAnotherWorkerFaults(bool cancelQueued)
    {
        WarpRuntimeModule module = RuntimeLifecycleTests.LoadModule();
        WarpRuntimeEntry entry = module.Entries[ManifestAssemblyFixture.MapEntryIdentity];
        var options = new WarpRuntimeOptions { MaximumConcurrentDispatches = 1, MaximumStepsPerWorker = 1 };
        var context = new WarpRuntimeContext(module, WarpBackendKind.CoreCLR, options);
        await using var contextLease = context.ConfigureAwait(false);
        using var cancellation = new CancellationTokenSource();
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(entry.Layout);
        var slots = (SemaphoreSlim)typeof(WarpRuntimeContext)
            .GetField("dispatchSlots", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!;
        await slots.WaitAsync().ConfigureAwait(false);
        Task<uint[]> queued;
        try
        {
            queued = context.DispatchIntegerMapAsync(entry.Identity, [Array.Empty<uint>()], [7], cancellation.Token);
            Assert.IsFalse(queued.IsCompleted);
            uint[] state = entry.Layout.CreateInitialState(options.MaximumCallDepth, options.MaximumStepsPerWorker);
            WarpRuntimeFaultException? fault = BindWorker(context)(entry, compiled, [[3]], [7], 0, state, new uint[1], CancellationToken.None);
            Assert.IsNotNull(fault);
            Assert.AreEqual(WarpRuntimeFaultKind.StepLimit, fault.Kind);
            Assert.AreEqual(WarpRuntimeContextState.Faulted, context.State);
            if (cancelQueued)
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            slots.Release();
        }

        try
        {
            await queued.ConfigureAwait(false);
            Assert.Fail("The queued dispatch must not publish after a worker fault was observed.");
        }
        catch (OperationCanceledException) when (cancelQueued)
        {
            Assert.IsTrue(queued.IsCanceled);
        }
        catch (WarpHostException error) when (!cancelQueued)
        {
            Assert.AreEqual("WRPRUNTIME1006", error.Code, StringComparer.Ordinal);
        }

        Assert.AreEqual(WarpRuntimeContextState.Faulted, context.State);
        Assert.AreEqual(0L, context.JitStatistics.CompilationCount);
    }

    [TestMethod]
    public void PackedResidentStateMustFitOneArrayBeforeAnyAllocationOrCompilation()
    {
        WarpLogicalMachineLayout layout = RuntimeLifecycleTests.LoadModule().Entries[ManifestAssemblyFixture.MapEntryIdentity].Layout;
        int stride = layout.GetStateWords(WarpRuntimeAbi.DefaultMaximumCallDepth);
        int largestCount = Array.MaxLength / stride;
        int firstUnrepresentableCount = checked(largestCount + 1);
        var options = new WarpRuntimeOptions
        {
            MaximumResidentWorkers = Math.Max(1_000_000, firstUnrepresentableCount),
            MaximumBufferBytes = long.MaxValue,
        };

        Assert.IsGreaterThan(0L, WarpDispatchResourceAdmission.EstimateBufferBytes(layout, largestCount, options));
        WarpHostException rejected = Assert.Throws<WarpHostException>(() =>
            WarpDispatchResourceAdmission.EstimateBufferBytes(layout, firstUnrepresentableCount, options));
        Assert.AreEqual("WRPRUNTIME1005", rejected.Code, StringComparer.Ordinal);
        Assert.IsGreaterThan(0L, WarpDispatchResourceAdmission.EstimateBufferBytes(layout, 1, options));
    }

    [TestMethod]
    public void PackedStateAdmissionUsesTheResidentWindowRatherThanTotalLogicalWorkers()
    {
        WarpLogicalMachineLayout layout = RuntimeLifecycleTests.LoadModule().Entries[ManifestAssemblyFixture.MapEntryIdentity].Layout;
        int stride = layout.GetStateWords(WarpRuntimeAbi.DefaultMaximumCallDepth);
        int countBeyondOnePackedArray = checked(Array.MaxLength / stride + 1);
        int representableWindow = Math.Min(1_000_000, countBeyondOnePackedArray - 1);
        var options = new WarpRuntimeOptions
        {
            MaximumResidentWorkers = representableWindow,
            MaximumBufferBytes = long.MaxValue,
        };

        Assert.IsGreaterThan(0L, WarpDispatchResourceAdmission.EstimateBufferBytes(layout, countBeyondOnePackedArray, options));
    }

    [TestMethod]
    [FourBackends]
    public async Task DisposedCallerCacheRejectsNewDispatchBeforeBackendStartup(WarpBackendKind backend)
    {
        var cache = new WarpJitCache();
        await using var cacheLease = cache.ConfigureAwait(false);
        await cache.DisposeAsync().ConfigureAwait(false);
        var context = new WarpRuntimeContext(RuntimeLifecycleTests.LoadModule(), backend, jitCache: cache);
        await using var contextLease = context.ConfigureAwait(false);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => context.DispatchIntegerMapAsync(
            ManifestAssemblyFixture.MapEntryIdentity, [new uint[] { 3 }], [7])).ConfigureAwait(false);

        Assert.AreEqual(0L, cache.Statistics.CompilationCount);
        Assert.AreEqual(0, cache.Statistics.MemoryEntryCount);
        Assert.AreEqual(WarpRuntimeContextState.Ready, context.State);
    }

    private static WorkerExecution BindWorker(WarpRuntimeContext context) => typeof(WarpRuntimeContext)
        .GetMethod("ExecuteWorker", BindingFlags.Instance | BindingFlags.NonPublic)!
        .CreateDelegate<WorkerExecution>(context);

    [TestMethod]
    public async Task ChangedInputReferencesAreRejectedBeforeCloneOrCompilation()
    {
        var context = new WarpRuntimeContext(RuntimeLifecycleTests.LoadModule(), WarpBackendKind.CoreCLR);
        await using var contextLease = context.ConfigureAwait(false);
        var inputs = new SwitchingInputList();

        WarpHostException rejected = await Assert.ThrowsAsync<WarpHostException>(() =>
            context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, inputs, [7])).ConfigureAwait(false);

        Assert.AreEqual("WRPRUNTIME1004", rejected.Code, StringComparer.Ordinal);
        Assert.AreEqual(0L, context.JitStatistics.CompilationCount);
        Assert.AreEqual(WarpRuntimeContextState.Ready, context.State);
        uint[] output = await context.DispatchIntegerMapAsync(
            ManifestAssemblyFixture.MapEntryIdentity, [new uint[] { 3 }], [7]).ConfigureAwait(false);
        Assert.AreEqual(106u, output[0]);
    }

    [TestMethod]
    public async Task ScalarSnapshotsUseOnlyTheAdmittedFixedIndexRange()
    {
        var context = new WarpRuntimeContext(RuntimeLifecycleTests.LoadModule(), WarpBackendKind.CoreCLR);
        await using var contextLease = context.ConfigureAwait(false);
        var scalars = new EnumeratorTrapScalarList();

        uint[] output = await context.DispatchIntegerMapAsync(
            ManifestAssemblyFixture.MapEntryIdentity, [new uint[] { 3 }], scalars).ConfigureAwait(false);

        Assert.AreEqual(106u, output[0]);
        Assert.AreEqual(1, scalars.IndexReads);
        Assert.AreEqual(WarpRuntimeContextState.Ready, context.State);
    }

    private delegate WarpRuntimeFaultException? WorkerExecution(
        WarpRuntimeEntry entry,
        CoreCLRResumableKernel compiled,
        uint[][] inputs,
        uint[] scalars,
        int worker,
        uint[] logicalState,
        uint[] output,
        CancellationToken cancellationToken);

    private sealed class SwitchingInputList : IReadOnlyList<uint[]>
    {
        private readonly uint[] admitted = [3];
        private readonly uint[] replaced = [3, 5];
        private int reads;

        public int Count => 1;

        public uint[] this[int index]
        {
            get
            {
                ArgumentOutOfRangeException.ThrowIfNotEqual(index, 0);
                return ++reads == 1 ? admitted : replaced;
            }
        }

        IEnumerator<uint[]> IEnumerable<uint[]>.GetEnumerator() => throw new InvalidOperationException("Input snapshotting must not enumerate caller code.");

        IEnumerator IEnumerable.GetEnumerator() => throw new InvalidOperationException("Input snapshotting must not enumerate caller code.");
    }

    private sealed class EnumeratorTrapScalarList : IReadOnlyList<uint>
    {
        public int Count => 1;

        public int IndexReads { get; private set; }

        public uint this[int index]
        {
            get
            {
                ArgumentOutOfRangeException.ThrowIfNotEqual(index, 0);
                IndexReads++;
                return 7;
            }
        }

        IEnumerator<uint> IEnumerable<uint>.GetEnumerator() => throw new InvalidOperationException("Scalar snapshotting must not enumerate caller code.");

        IEnumerator IEnumerable.GetEnumerator() => throw new InvalidOperationException("Scalar snapshotting must not enumerate caller code.");
    }
}
