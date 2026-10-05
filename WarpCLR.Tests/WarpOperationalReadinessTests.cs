using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Tests.Production;

namespace WarpCLR.Tests;

[TestClass]
[TestCategory("CoreCLROperational")]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest discovers and executes this operational fixture through reflection.")]
internal sealed class WarpOperationalReadinessTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(false, 31, 1)]
    [DataRow(true, 257, 4)]
    public async Task RepeatedQueuePressureReturnsEveryReservationAndPreservesSnapshots(
        bool exhaustBufferBytes, int residentWorkers, int parallelWorkers)
    {
        const int workers = 1031;
        const int queuedCount = 8;
        WarpRuntimeModule module = RuntimeLifecycleTests.LoadModule();
        var options = new WarpRuntimeOptions
        {
            MaximumConcurrentDispatches = 2,
            MaximumAdmittedDispatches = exhaustBufferBytes ? queuedCount + 1 : queuedCount,
            MaximumCallDepth = 2,
            MaximumResidentWorkers = residentWorkers,
            MaximumParallelWorkers = parallelWorkers,
            ExecutionQuantum = module.Entries.Values.Max(entry => entry.Layout.MaximumBlockCost),
        };
        long reservation = WarpDispatchResourceAdmission.EstimateBufferBytes(
            module.Entries[ManifestAssemblyFixture.MapEntryIdentity].Layout, workers, options);
        var cache = new WarpJitCache(new WarpJitCacheOptions { MaximumMemoryEntries = 1 });
        await using var cacheLease = cache.ConfigureAwait(false);
        var context = new WarpRuntimeContext(module, WarpBackendKind.CoreCLR,
            options with { MaximumBufferBytes = reservation * (exhaustBufferBytes ? queuedCount : queuedCount + 1) }, cache);
        await using var contextLease = context.ConfigureAwait(false);
        int rounds = Rounds;
        var clock = Stopwatch.StartNew();
        for (int round = 0; round < rounds; round++)
        {
            await RunPressureRoundAsync(context, round, workers, reservation).ConfigureAwait(false);
            uint[] input = Input(workers, round);
            uint sum = 0;
            foreach (uint value in input)
            {
                sum = unchecked(sum + TestKernels.ManifestReduction(value, (uint)round));
            }

            Assert.AreEqual(sum, await context.DispatchUInt32ReductionAsync(
                ManifestAssemblyFixture.ReductionEntryIdentity, [input], [(uint)round]).ConfigureAwait(false));
            AssertDrained(context, 2);
            Assert.AreEqual((round + 1) * 2L, cache.Statistics.CompilationCount);
            Assert.AreEqual(1, cache.Statistics.MemoryEntryCount);
        }

        TestContext.WriteLine($"Queue soak: {rounds} rounds, {rounds * 9} admitted requests, {rounds} admission rejections, {rounds * 2} cancellations, " +
            $"{rounds * 7L * workers} completed workers, {clock.Elapsed}, buffer pressure={exhaustBufferBytes}.");
    }

    private static async Task RunPressureRoundAsync(WarpRuntimeContext context, int round, int workers, long reservation)
    {
        const int queuedCount = 8;
        SemaphoreSlim slots = Field<SemaphoreSlim>(context, "dispatchSlots");
        using var cancellation = new CancellationTokenSource();
        Task<uint[]>[] dispatches = new Task<uint[]>[queuedCount];
        uint[][] expected = new uint[queuedCount][];
        await slots.WaitAsync().ConfigureAwait(false);
        await slots.WaitAsync().ConfigureAwait(false);
        try
        {
            QueueSnapshotDispatches(context, round, workers, dispatches, expected, cancellation.Token);
            Assert.AreEqual(queuedCount, Field<int>(context, "activeDispatches"));
            Assert.AreEqual(reservation * queuedCount, Field<long>(context, "admittedBytes"));
            WarpHostException rejection = await Assert.ThrowsAsync<WarpHostException>(() =>
                context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [new uint[workers]], [0])).ConfigureAwait(false);
            Assert.AreEqual("WRPRUNTIME1005", rejection.Code, StringComparer.Ordinal);
            Assert.AreEqual(round * 2L, context.JitStatistics.CompilationCount);
            await cancellation.CancelAsync().ConfigureAwait(false);
            await Assert.ThrowsAsync<OperationCanceledException>(() => dispatches[0]).ConfigureAwait(false);
            await Assert.ThrowsAsync<OperationCanceledException>(() => dispatches[^1]).ConfigureAwait(false);
            Assert.IsTrue(dispatches[0].IsCanceled);
            Assert.IsTrue(dispatches[^1].IsCanceled);
            Assert.AreEqual(queuedCount - 2, Field<int>(context, "activeDispatches"));
            Assert.AreEqual(reservation * (queuedCount - 2), Field<long>(context, "admittedBytes"));
        }
        finally
        {
            slots.Release(2);
        }

        for (int dispatch = 1; dispatch < queuedCount - 1; dispatch++)
        {
            CollectionAssert.AreEqual(expected[dispatch], await dispatches[dispatch].ConfigureAwait(false));
        }
    }

    private static void QueueSnapshotDispatches(WarpRuntimeContext context, int round, int workers,
        Task<uint[]>[] dispatches, uint[][] expected, CancellationToken cancellation)
    {
        for (int dispatch = 0; dispatch < dispatches.Length; dispatch++)
        {
            uint[] input = Input(workers, round + dispatch);
            uint[] scalars = [unchecked(uint.MaxValue - (uint)(round + dispatch))];
            expected[dispatch] = input.Select(value => TestKernels.ManifestMap(value, scalars[0])).ToArray();
            CancellationToken token = dispatch == 0 || dispatch == dispatches.Length - 1 ? cancellation : CancellationToken.None;
            dispatches[dispatch] = context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [input], scalars, token);
            Assert.IsFalse(dispatches[dispatch].IsCompleted);
            Array.Fill(input, 0xDEADBEEFu);
            scalars[0] = 0;
        }
    }

    [TestMethod]
    public async Task MillionWorkerQueuedBuffersSaturateTheRealQuotaAndRecoverWithoutFallback()
    {
        const int workers = 1_048_579;
        WarpRuntimeModule module = RuntimeLifecycleTests.LoadModule();
        var options = new WarpRuntimeOptions
        {
            MaximumConcurrentDispatches = 2,
            MaximumAdmittedDispatches = 9,
            MaximumResidentWorkers = 257,
            MaximumParallelWorkers = 4,
            MaximumCallDepth = 2,
            ExecutionQuantum = 65536,
        };
        long reservation = WarpDispatchResourceAdmission.EstimateBufferBytes(
            module.Entries[ManifestAssemblyFixture.MapEntryIdentity].Layout, workers, options);
        var cache = new WarpJitCache(new WarpJitCacheOptions { MaximumMemoryEntries = 1 });
        await using var cacheLease = cache.ConfigureAwait(false);
        var context = new WarpRuntimeContext(module, WarpBackendKind.CoreCLR,
            options with { MaximumBufferBytes = reservation * 8 }, cache);
        await using var contextLease = context.ConfigureAwait(false);
        var clock = Stopwatch.StartNew();
        await RunPressureRoundAsync(context, 0, workers, reservation).ConfigureAwait(false);
        AssertDrained(context, 2);
        uint[] input = Input(workers, 0);
        uint expected = 0;
        foreach (uint value in input)
        {
            expected = unchecked(expected + TestKernels.ManifestReduction(value, 0));
        }

        Assert.AreEqual(expected, await context.DispatchUInt32ReductionAsync(
            ManifestAssemblyFixture.ReductionEntryIdentity, [input], [0]).ConfigureAwait(false));
        AssertDrained(context, 2);
        Assert.AreEqual(2L, cache.Statistics.CompilationCount);
        TestContext.WriteLine($"Million-worker pressure: {reservation * 8} reserved bytes at saturation, " +
            $"{workers * 7L} completed workers, two queued cancellations, one admission rejection, {clock.Elapsed}. " +
            "This saturates the common buffer-data quota, not the OS memory limit or a whole-process reservation.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RepeatedRealLoopFaultsQuarantineAndNewContextsRecoverThroughTheSameJit(bool largeQuantum)
    {
        using var fixture = new WarpBooleanProofFixture();
        WarpRuntimeModule module = Load(fixture.Bytes);
        string identity = WarpBooleanProofFixture.Identity("Loop");
        Func<uint, uint, uint> original = fixture.Method("Loop").CreateDelegate<Func<uint, uint, uint>>();
        var cache = new WarpJitCache(new WarpJitCacheOptions { MaximumMemoryEntries = 1 });
        await using var cacheLease = cache.ConfigureAwait(false);
        var options = new WarpRuntimeOptions
        {
            MaximumConcurrentDispatches = 1,
            MaximumCallDepth = 1,
            MaximumResidentWorkers = 31,
            MaximumParallelWorkers = 4,
            ExecutionQuantum = largeQuantum ? 65536 : module.Entries[identity].Layout.MaximumBlockCost,
        };
        int rounds = Rounds;
        var clock = Stopwatch.StartNew();
        for (int round = 0; round < rounds; round++)
        {
            uint[] input = Input(129, round);
            uint[] before = input.ToArray();
            var faulted = new WarpRuntimeContext(module, WarpBackendKind.CoreCLR,
                options with { MaximumStepsPerWorker = 64 }, cache);
            await using var faultedLease = faulted.ConfigureAwait(false);
            Task<uint[]>? unpublished = null;
            WarpRuntimeFaultException fault = await Assert.ThrowsAsync<WarpRuntimeFaultException>(() =>
                unpublished = faulted.DispatchIntegerMapAsync(identity, [input], [uint.MaxValue])).ConfigureAwait(false);
            Assert.IsNotNull(unpublished);
            Assert.IsTrue(unpublished.IsFaulted);
            Assert.AreEqual(WarpRuntimeFaultKind.StepLimit, fault.Kind);
            Assert.AreEqual(0, fault.WorkerIndex);
            Assert.AreEqual(identity, fault.FunctionIdentity, StringComparer.Ordinal);
            Assert.AreEqual(WarpRuntimeContextState.Faulted, faulted.State);
            CollectionAssert.AreEqual(before, input);
            Assert.AreEqual(0, Field<int>(faulted, "activeDispatches"));
            Assert.AreEqual(0L, Field<long>(faulted, "admittedBytes"));
            WarpHostException quarantine = await Assert.ThrowsAsync<WarpHostException>(() =>
                faulted.DispatchIntegerMapAsync(identity, [input], [0])).ConfigureAwait(false);
            Assert.AreEqual("WRPRUNTIME1006", quarantine.Code, StringComparer.Ordinal);
            await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => faulted.DisposeAsync().AsTask())).ConfigureAwait(false);
            Assert.AreEqual(WarpRuntimeContextState.Disposed, faulted.State);
            var recovered = new WarpRuntimeContext(module, WarpBackendKind.CoreCLR, options, cache);
            await using var recoveredLease = recovered.ConfigureAwait(false);
            uint iterations = (uint)(round % 257);
            uint[] expected = input.Select(value => original(value, iterations)).ToArray();
            CollectionAssert.AreEqual(expected, await recovered.DispatchIntegerMapAsync(identity, [input], [iterations]).ConfigureAwait(false));
            AssertDrained(recovered, 1);
            Assert.AreEqual(1L, cache.Statistics.CompilationCount);
            Assert.AreEqual(1, cache.Statistics.MemoryEntryCount);
        }

        TestContext.WriteLine($"Fault/recreation soak: {rounds} escaped original-CIL loop faults and {rounds} original-CLR differential recoveries, " +
            $"{clock.Elapsed}, large quantum={largeQuantum}, one reused JIT compilation.");
    }

    [TestMethod]
    public async Task LongRunningLoopCancellationReturnsAdmissionAndQuarantinesStartedExecution()
    {
        using var fixture = new WarpBooleanProofFixture();
        WarpRuntimeModule module = Load(fixture.Bytes);
        string identity = WarpBooleanProofFixture.Identity("Loop");
        Func<uint, uint, uint> original = fixture.Method("Loop").CreateDelegate<Func<uint, uint, uint>>();
        var runtimeOptions = new WarpRuntimeOptions
        {
            MaximumConcurrentDispatches = 1,
            MaximumStepsPerWorker = long.MaxValue,
            MaximumCallDepth = 1,
            MaximumResidentWorkers = 3,
            MaximumParallelWorkers = 1,
            MaximumAdmittedDispatches = 1,
            ExecutionQuantum = module.Entries[identity].Layout.MaximumBlockCost,
        };
        var context = new WarpRuntimeContext(module, WarpBackendKind.CoreCLR, runtimeOptions);
        await using var contextLease = context.ConfigureAwait(false);
        uint[] input = Input(7, 0);
        uint[] expected = input.Select(value => original(value, 17)).ToArray();
        CollectionAssert.AreEqual(expected, await context.DispatchIntegerMapAsync(identity, [input], [17]).ConfigureAwait(false));
        int rounds = Rounds;
        var clock = Stopwatch.StartNew();
        for (int round = 0; round < rounds; round++)
        {
            using var cancellation = new CancellationTokenSource();
            Task<uint[]> running = context.DispatchIntegerMapAsync(identity, [input], [uint.MaxValue], cancellation.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);
            Assert.IsFalse(running.IsCompleted);
            await cancellation.CancelAsync().ConfigureAwait(false);
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await running.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.IsTrue(running.IsCanceled);
            AssertDrained(context, 1, allowFaulted: true);
            if (context.State == WarpRuntimeContextState.Faulted)
            {
                await Assert.ThrowsAsync<WarpHostException>(() => context.DispatchIntegerMapAsync(identity, [input], [17])).ConfigureAwait(false);
                break;
            }
            CollectionAssert.AreEqual(expected, await context.DispatchIntegerMapAsync(identity, [input], [17]).ConfigureAwait(false));
            Assert.AreEqual(1L, context.JitStatistics.CompilationCount);
        }

        var fresh = new WarpRuntimeContext(module, WarpBackendKind.CoreCLR, runtimeOptions);
        await using var freshOwner = fresh.ConfigureAwait(false);
        CollectionAssert.AreEqual(expected, await fresh.DispatchIntegerMapAsync(identity, [input], [17]).ConfigureAwait(false));
        TestContext.WriteLine($"Cancellation gate: pre-start cancellations may reuse admission, started execution permanently faults its context; fresh ownership recovers in {clock.Elapsed}. " +
            "The delay is a scheduling opportunity; the exact stopped-child tests prove native transaction ownership independently.");
    }

    [TestMethod]
    public async Task RepeatedOwnedContextDisposalReleasesCollectibleCodeEvenWithDisposedContextsRetained()
    {
        WarpRuntimeModule module = RuntimeLifecycleTests.LoadModule();
        var contexts = new List<WarpRuntimeContext>();
        var processes = new List<int>();
        int rounds = Rounds;
        var clock = Stopwatch.StartNew();
        for (int round = 0; round < rounds; round++)
        {
            var context = new WarpRuntimeContext(module, WarpBackendKind.CoreCLR,
                new WarpRuntimeOptions { MaximumResidentWorkers = 17, MaximumCallDepth = 2 });
            await using var contextLease = context.ConfigureAwait(false);
            contexts.Add(context);
            uint[] input = Input(129, round);
            CollectionAssert.AreEqual(input.Select(value => TestKernels.ManifestMap(value, (uint)round)).ToArray(),
                await context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [input], [(uint)round]).ConfigureAwait(false));
            processes.Add(await WarpCacheLifetimeProbe.CaptureCompiledProcessAsync(context, module).ConfigureAwait(false));
            await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => context.DisposeAsync().AsTask())).ConfigureAwait(false);
            Assert.AreEqual(WarpRuntimeContextState.Disposed, context.State);
            Assert.AreEqual(0, context.JitStatistics.MemoryEntryCount);
            Assert.AreEqual(0, Field<int>(context, "activeDispatches"));
            Assert.AreEqual(0L, Field<long>(context, "admittedBytes"));
        }

        foreach (ref readonly int processId in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(processes))
        {
            try { using Process child = Process.GetProcessById(processId); Assert.IsTrue(child.HasExited); }
            catch (ArgumentException) { }
        }

        GC.KeepAlive(contexts);
        TestContext.WriteLine($"Owned-cache churn: {rounds} separately compiled/disposed contexts retained, all {rounds} generated worker processes terminated, {clock.Elapsed}.");
    }

    private static int Rounds
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable("WARPCLR_CORECLR_SOAK_ROUNDS");
            if (configured is null)
            {
                return 16;
            }

            if (!int.TryParse(configured, NumberStyles.None, CultureInfo.InvariantCulture, out int rounds) || rounds is < 1 or > 4096)
            {
                throw new InvalidOperationException("WARPCLR_CORECLR_SOAK_ROUNDS must be an integer from 1 through 4096.");
            }

            return rounds;
        }
    }

    private static uint[] Input(int count, int seed) => Enumerable.Range(0, count)
        .Select(index => unchecked((uint)index * 0xDEADBEEFu + (uint)seed * 257u)).ToArray();

    private static WarpRuntimeModule Load(byte[] bytes) =>
        WarpRuntimeModule.Load(bytes, new WarpModuleTrust([Convert.ToHexString(SHA256.HashData(bytes))]));

    private static T Field<T>(WarpRuntimeContext context, string name) =>
        (T)typeof(WarpRuntimeContext).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!;

    private static void AssertDrained(WarpRuntimeContext context, int slots, bool allowFaulted = false)
    {
        Assert.IsTrue(context.State == WarpRuntimeContextState.Ready || allowFaulted && context.State == WarpRuntimeContextState.Faulted);
        Assert.AreEqual(0, Field<int>(context, "activeDispatches"));
        Assert.AreEqual(0L, Field<long>(context, "admittedBytes"));
        Assert.AreEqual(slots, Field<SemaphoreSlim>(context, "dispatchSlots").CurrentCount);
    }
}
