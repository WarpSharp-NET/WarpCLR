using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Reflection;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers and instantiates this fixture through reflection.")]
internal sealed class RuntimeLifecycleTests
{
    [TestMethod]
    public async Task TrustedModuleExecutesThroughRealJitWithoutAotOrEmulation()
    {
        WarpRuntimeModule module = LoadModule();
        var context = new WarpRuntimeContext(module, WarpBackendKind.CoreCLR);
        await using var contextLease = context.ConfigureAwait(false);
        uint[] input = [0, 1, uint.MaxValue, 0x80000000, 0x12345678];
        uint[] output = await context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [input], [17]).ConfigureAwait(false);
        CollectionAssert.AreEqual(input.Select(value => TestKernels.ManifestMap(value, 17)).ToArray(), output);
        Assert.AreEqual(1, context.JitStatistics.CompilationCount);
        Assert.AreEqual(WarpRuntimeContextState.Ready, context.State);
    }

    [TestMethod]
    public void UntrustedAndModifiedAssembliesAreRejectedBeforeJit()
    {
        byte[] bytes = ManifestAssemblyFixture.ReadAssembly();
        var emptyTrust = new WarpModuleTrust([]);
        WarpHostException rejected = Assert.Throws<WarpHostException>(() => WarpRuntimeModule.Load(bytes, emptyTrust));
        Assert.AreEqual("WRPRUNTIME1000", rejected.Code, StringComparer.Ordinal);
        var trust = new WarpModuleTrust([Convert.ToHexString(SHA256.HashData(bytes))]);
        bytes[^1] ^= 1;
        Assert.Throws<WarpHostException>(() => WarpRuntimeModule.Load(bytes, trust));
    }

    [TestMethod]
    public void TrustPolicyCannotAcceptNonSha256OrPathIdentities()
    {
        Assert.Throws<ArgumentException>(() => new WarpModuleTrust(["assembly.dll"]));
        Assert.Throws<ArgumentException>(() => new WarpModuleTrust([new string('Z', 64)]));
    }

    [TestMethod]
    public async Task ConcurrentDispatchesShareASingleCompilation()
    {
        var context = new WarpRuntimeContext(LoadModule(), WarpBackendKind.CoreCLR,
            new WarpRuntimeOptions { MaximumConcurrentDispatches = 32 });
        await using var contextLease = context.ConfigureAwait(false);
        Task<uint[]>[] dispatches = Enumerable.Range(0, 32).Select(index =>
            context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [new uint[] { (uint)index }], [7])).ToArray();
        uint[][] outputs = await Task.WhenAll(dispatches).ConfigureAwait(false);
        for (int index = 0; index < outputs.Length; index++)
        {
            Assert.AreEqual(TestKernels.ManifestMap((uint)index, 7), outputs[index][0]);
        }

        Assert.AreEqual(1, context.JitStatistics.CompilationCount);
        Assert.AreEqual(31, context.JitStatistics.MemoryHitCount);
    }

    [TestMethod]
    public async Task QueuedDispatchOwnsInputAndScalarSnapshotsBeforeItsTaskIsReturned()
    {
        var context = new WarpRuntimeContext(LoadModule(), WarpBackendKind.CoreCLR,
            new WarpRuntimeOptions { MaximumConcurrentDispatches = 1 });
        await using var contextLease = context.ConfigureAwait(false);
        var slots = (SemaphoreSlim)typeof(WarpRuntimeContext).GetField("dispatchSlots", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!;
        await slots.WaitAsync().ConfigureAwait(false);
        Task<uint[]> dispatch;
        try
        {
            uint[] input = [3];
            uint[] scalars = [7];
            dispatch = context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [input], scalars);
            Assert.IsFalse(dispatch.IsCompleted);
            input[0] = uint.MaxValue;
            scalars[0] = uint.MaxValue;
        }
        finally
        {
            slots.Release();
        }

        uint[] result = await dispatch.ConfigureAwait(false);
        Assert.AreEqual(106u, result[0]);
    }

    [TestMethod]
    public async Task StepFaultSelectsLowestLogicalWorkerSuppressesOutputAndInvalidatesContext()
    {
        var context = new WarpRuntimeContext(LoadModule(), WarpBackendKind.CoreCLR,
            new WarpRuntimeOptions { MaximumStepsPerWorker = 1 });
        await using var contextLease = context.ConfigureAwait(false);
        uint[] input = [1, 2, 3, 4];
        WarpRuntimeFaultException fault = await Assert.ThrowsAsync<WarpRuntimeFaultException>(() =>
            context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [input], [0])).ConfigureAwait(false);
        Assert.AreEqual(0, fault.WorkerIndex);
        Assert.AreEqual(WarpRuntimeFaultKind.StepLimit, fault.Kind);
        Assert.AreEqual(0, fault.FunctionIndex);
        Assert.AreEqual(ManifestAssemblyFixture.MapEntryIdentity, fault.FunctionIdentity, StringComparer.Ordinal);
        Assert.AreEqual(0, fault.BlockIndex);
        Assert.AreEqual(1UL, fault.RemainingSteps);
        CollectionAssert.AreEqual(new uint[] { 1, 2, 3, 4 }, input);
        Assert.AreEqual(WarpRuntimeContextState.Faulted, context.State);
        WarpHostException invalidated = await Assert.ThrowsAsync<WarpHostException>(() =>
            context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [input], [0])).ConfigureAwait(false);
        Assert.AreEqual("WRPRUNTIME1006", invalidated.Code, StringComparer.Ordinal);
    }

    [TestMethod]
    public async Task LogicalCallDepthIsNotChangedByHostJitInlining()
    {
        WarpRuntimeModule module = LoadModule();
        WarpLogicalMachineLayout layout = module.Entries[ManifestAssemblyFixture.MapEntryIdentity].Layout;
        Assert.AreEqual(3, layout.Nodes[0].BlockCost);
        Assert.AreEqual(2, layout.Nodes[1].BlockCost);
        Assert.AreEqual(1, layout.Nodes[1].Block);
        Assert.AreEqual(WarpIrOpCode.Call, layout.Nodes[1].Call?.OpCode);
        var context = new WarpRuntimeContext(module, WarpBackendKind.CoreCLR,
            new WarpRuntimeOptions { MaximumCallDepth = 1 });
        await using var contextLease = context.ConfigureAwait(false);
        WarpRuntimeFaultException fault = await Assert.ThrowsAsync<WarpRuntimeFaultException>(() =>
            context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [new uint[] { 5 }], [0])).ConfigureAwait(false);
        Assert.AreEqual(WarpRuntimeFaultKind.CallDepth, fault.Kind);
        Assert.AreEqual(0, fault.FunctionIndex);
        Assert.AreEqual(ManifestAssemblyFixture.MapEntryIdentity, fault.FunctionIdentity, StringComparer.Ordinal);
        Assert.AreEqual(1, fault.BlockIndex);
        Assert.AreEqual((ulong)WarpRuntimeAbi.DefaultMaximumStepsPerWorker - 5, fault.RemainingSteps);
        Assert.AreEqual(WarpRuntimeContextState.Faulted, context.State);
    }

    [TestMethod]
    public async Task FailedBufferAdmissionDoesNotCompileOrTaintTheContext()
    {
        WarpRuntimeModule module = LoadModule();
        var options = new WarpRuntimeOptions { MaximumCallDepth = 2, MaximumResidentWorkers = 1 };
        long reservation = WarpDispatchResourceAdmission.EstimateBufferBytes(module.Entries[ManifestAssemblyFixture.MapEntryIdentity].Layout, 1, options);
        var context = new WarpRuntimeContext(module, WarpBackendKind.CoreCLR, options with { MaximumBufferBytes = reservation + 1 });
        await using var contextLease = context.ConfigureAwait(false);
        WarpHostException error = await Assert.ThrowsAsync<WarpHostException>(() =>
            context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [new uint[8]], [0])).ConfigureAwait(false);
        Assert.AreEqual("WRPRUNTIME1005", error.Code, StringComparer.Ordinal);
        Assert.AreEqual(0, context.JitStatistics.CompilationCount);
        uint[] output = await context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [new uint[1]], [5]).ConfigureAwait(false);
        Assert.AreEqual(5u, output[0]);
        Assert.AreEqual(WarpRuntimeContextState.Ready, context.State);
    }

    [TestMethod]
    public async Task QueuedDispatchReservesItsWholeWorkingSetAndCancellationReturnsTheReservation()
    {
        WarpRuntimeModule module = LoadModule();
        var options = new WarpRuntimeOptions { MaximumConcurrentDispatches = 1, MaximumCallDepth = 2, MaximumResidentWorkers = 1 };
        WarpRuntimeEntry entry = module.Entries[ManifestAssemblyFixture.MapEntryIdentity];
        long reservation = WarpDispatchResourceAdmission.EstimateBufferBytes(entry.Layout, 1, options);
        var context = new WarpRuntimeContext(module, WarpBackendKind.CoreCLR, options with { MaximumBufferBytes = reservation * 2 - 1 });
        await using var contextLease = context.ConfigureAwait(false);
        using var cancellation = new CancellationTokenSource();
        var slots = (SemaphoreSlim)typeof(WarpRuntimeContext).GetField("dispatchSlots", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!;
        await slots.WaitAsync().ConfigureAwait(false);
        try
        {
            Task<uint[]> queued = context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [new uint[] { 3 }], [7], cancellation.Token);
            Assert.IsFalse(queued.IsCompleted);
            WarpHostException rejected = await Assert.ThrowsAsync<WarpHostException>(() =>
                context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [new uint[] { 3 }], [7])).ConfigureAwait(false);
            Assert.AreEqual("WRPRUNTIME1005", rejected.Code, StringComparer.Ordinal);
            Assert.AreEqual(0, context.JitStatistics.CompilationCount);
            await cancellation.CancelAsync().ConfigureAwait(false);
            try
            {
                await queued.ConfigureAwait(false);
                Assert.Fail("The queued dispatch was expected to be cancelled.");
            }
            catch (OperationCanceledException)
            {
                Assert.IsTrue(queued.IsCanceled);
            }
        }
        finally
        {
            slots.Release();
        }

        uint[] result = await context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [new uint[] { 3 }], [7]).ConfigureAwait(false);
        Assert.AreEqual(106u, result[0]);
        Assert.AreEqual(WarpRuntimeContextState.Ready, context.State);
    }

    [TestMethod]
    public async Task ShortFinalResidentBatchDoesNotReuseAnEarlierWorkersResults()
    {
        var context = new WarpRuntimeContext(LoadModule(), WarpBackendKind.CoreCLR,
            new WarpRuntimeOptions { MaximumResidentWorkers = 3, ExecutionQuantum = 16 });
        await using var contextLease = context.ConfigureAwait(false);
        uint[] input = [0, 1, 2, 3, 4, 5, uint.MaxValue];
        uint[] output = await context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [input], [7]).ConfigureAwait(false);
        CollectionAssert.AreEqual(input.Select(value => TestKernels.ManifestMap(value, 7)).ToArray(), output);
    }

    [TestMethod]
    public async Task DispatchCountAdmissionBoundsZeroLengthRequestsEvenWhenBufferPayloadIsNegligible()
    {
        var context = new WarpRuntimeContext(LoadModule(), WarpBackendKind.CoreCLR,
            new WarpRuntimeOptions { MaximumConcurrentDispatches = 1, MaximumAdmittedDispatches = 1 });
        await using var contextLease = context.ConfigureAwait(false);
        using var cancellation = new CancellationTokenSource();
        var slots = (SemaphoreSlim)typeof(WarpRuntimeContext).GetField("dispatchSlots", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!;
        await slots.WaitAsync().ConfigureAwait(false);
        Task<uint[]> queued;
        try
        {
            queued = context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [Array.Empty<uint>()], [0], cancellation.Token);
            Assert.IsFalse(queued.IsCompleted);
            WarpHostException rejected = await Assert.ThrowsAsync<WarpHostException>(() => context.DispatchIntegerMapAsync(
                ManifestAssemblyFixture.MapEntryIdentity, [Array.Empty<uint>()], [0])).ConfigureAwait(false);
            Assert.AreEqual("WRPRUNTIME1005", rejected.Code, StringComparer.Ordinal);
            Assert.AreEqual(0, context.JitStatistics.CompilationCount);
            await cancellation.CancelAsync().ConfigureAwait(false);
        }
        finally
        {
            slots.Release();
        }

        try
        {
            await queued.ConfigureAwait(false);
            Assert.Fail("The cancelled queued request must not complete successfully.");
        }
        catch (OperationCanceledException)
        {
        }

        uint[] result = await context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [Array.Empty<uint>()], [0]).ConfigureAwait(false);
        Assert.IsEmpty(result);
        Assert.AreEqual(WarpRuntimeContextState.Ready, context.State);
    }

    [TestMethod]
    public async Task PrecancelledDispatchDoesNotCompileOrTaintContext()
    {
        var context = new WarpRuntimeContext(LoadModule(), WarpBackendKind.CoreCLR);
        await using var contextLease = context.ConfigureAwait(false);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(false);
        await Assert.ThrowsAsync<OperationCanceledException>(() => context.DispatchIntegerMapAsync(
            ManifestAssemblyFixture.MapEntryIdentity, [new uint[1]], [0], cancellation.Token)).ConfigureAwait(false);
        Assert.AreEqual(0, context.JitStatistics.CompilationCount);
        Assert.AreEqual(WarpRuntimeContextState.Ready, context.State);
    }

    [TestMethod]
    public async Task AsyncDisposalDrainsDispatchesAndIsIdempotentUnderConcurrency()
    {
        var context = new WarpRuntimeContext(LoadModule(), WarpBackendKind.CoreCLR);
        await using var contextLease = context.ConfigureAwait(false);
        Task<uint[]> dispatch = context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [new uint[1_000_000]], [0]);
        Task[] disposals = Enumerable.Range(0, 8).Select(_ => context.DisposeAsync().AsTask()).ToArray();
        await Task.WhenAll(disposals).ConfigureAwait(false);
        try
        {
            await dispatch.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        Assert.AreEqual(WarpRuntimeContextState.Disposed, context.State);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => context.DispatchIntegerMapAsync(
            ManifestAssemblyFixture.MapEntryIdentity, [new uint[1]], [0])).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task LargeReductionMapsParallelWorkersAndReducesMultiplePassesExactly()
    {
        var context = new WarpRuntimeContext(LoadModule(), WarpBackendKind.CoreCLR);
        await using var contextLease = context.ConfigureAwait(false);
        uint[] input = Enumerable.Range(0, 1_048_579).Select(index => unchecked((uint)index * 0xDEADBEEFu)).ToArray();
        uint expected = 0;
        foreach (uint value in input)
        {
            expected = unchecked(expected + TestKernels.ManifestReduction(value, 23));
        }

        uint actual = await context.DispatchUInt32ReductionAsync(ManifestAssemblyFixture.ReductionEntryIdentity, [input], [23]).ConfigureAwait(false);
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public async Task EmptyMapAndReductionObserveDefinedIdentities()
    {
        var context = new WarpRuntimeContext(LoadModule(), WarpBackendKind.CoreCLR);
        await using var contextLease = context.ConfigureAwait(false);
        uint[] output = await context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [Array.Empty<uint>()], [0]).ConfigureAwait(false);
        Assert.IsEmpty(output);
        Assert.AreEqual(0u, await context.DispatchUInt32ReductionAsync(ManifestAssemblyFixture.ReductionEntryIdentity, [Array.Empty<uint>()], [0]).ConfigureAwait(false));
    }

    [TestMethod]
    public void AnUnregisteredBackendIsAnExplicitErrorNeverCpuFallback()
    {
        WarpRuntimeModule module = LoadModule();
        WarpHostException error = Assert.Throws<WarpHostException>(() => new WarpRuntimeContext(module, (WarpBackendKind)int.MaxValue));
        Assert.AreEqual("WRPRUNTIME1003", error.Code, StringComparer.Ordinal);
    }

    internal static WarpRuntimeModule LoadModule()
    {
        byte[] bytes = ManifestAssemblyFixture.ReadAssembly();
        return WarpRuntimeModule.Load(bytes, new WarpModuleTrust([Convert.ToHexString(SHA256.HashData(bytes))]));
    }
}
