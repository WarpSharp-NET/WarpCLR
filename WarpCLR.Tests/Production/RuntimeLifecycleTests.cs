using System.Security.Cryptography;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests.Production;

[TestClass]
public sealed class RuntimeLifecycleTests
{
    [TestMethod]
    public async Task Trusted_module_executes_through_real_jit_without_aot_or_emulation()
    {
        WarpRuntimeModule module = LoadModule();
        await using var context = new WarpRuntimeContext(module, WarpBackendKind.CoreCLR);
        uint[] input = [0, 1, uint.MaxValue, 0x80000000, 0x12345678];
        uint[] output = await context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [input], [17]);
        CollectionAssert.AreEqual(input.Select(value => TestKernels.ManifestMap(value, 17)).ToArray(), output);
        Assert.AreEqual(1, context.JitStatistics.CompilationCount);
        Assert.AreEqual(WarpRuntimeContextState.Ready, context.State);
    }

    [TestMethod]
    public void Untrusted_and_modified_assemblies_are_rejected_before_jit()
    {
        byte[] bytes = ManifestAssemblyFixture.ReadAssembly();
        var emptyTrust = new WarpModuleTrust([]);
        WarpHostException rejected = Assert.Throws<WarpHostException>(() => WarpRuntimeModule.Load(bytes, emptyTrust));
        Assert.AreEqual("WRPRUNTIME1000", rejected.Code);
        var trust = new WarpModuleTrust([Convert.ToHexString(SHA256.HashData(bytes))]);
        bytes[^1] ^= 1;
        Assert.Throws<WarpHostException>(() => WarpRuntimeModule.Load(bytes, trust));
    }

    [TestMethod]
    public void Trust_policy_cannot_accept_non_sha256_or_path_identities()
    {
        Assert.Throws<ArgumentException>(() => new WarpModuleTrust(["assembly.dll"]));
        Assert.Throws<ArgumentException>(() => new WarpModuleTrust([new string('Z', 64)]));
    }

    [TestMethod]
    public async Task Concurrent_dispatches_share_a_single_compilation()
    {
        await using var context = new WarpRuntimeContext(LoadModule(), WarpBackendKind.CoreCLR,
            new WarpRuntimeOptions { MaximumConcurrentDispatches = 32 });
        Task<uint[]>[] dispatches = Enumerable.Range(0, 32).Select(index =>
            context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [new uint[] { (uint)index }], [7])).ToArray();
        uint[][] outputs = await Task.WhenAll(dispatches);
        for (int index = 0; index < outputs.Length; index++)
        {
            Assert.AreEqual(TestKernels.ManifestMap((uint)index, 7), outputs[index][0]);
        }

        Assert.AreEqual(1, context.JitStatistics.CompilationCount);
        Assert.AreEqual(31, context.JitStatistics.MemoryHitCount);
    }

    [TestMethod]
    public async Task Step_fault_selects_lowest_logical_worker_suppresses_output_and_invalidates_context()
    {
        await using var context = new WarpRuntimeContext(LoadModule(), WarpBackendKind.CoreCLR,
            new WarpRuntimeOptions { MaximumStepsPerWorker = 1 });
        uint[] input = [1, 2, 3, 4];
        WarpRuntimeFaultException fault = await Assert.ThrowsAsync<WarpRuntimeFaultException>(() =>
            context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [input], [0]));
        Assert.AreEqual(0, fault.WorkerIndex);
        Assert.AreEqual(WarpRuntimeFaultKind.StepLimit, fault.Kind);
        CollectionAssert.AreEqual(new uint[] { 1, 2, 3, 4 }, input);
        Assert.AreEqual(WarpRuntimeContextState.Faulted, context.State);
        WarpHostException invalidated = await Assert.ThrowsAsync<WarpHostException>(() =>
            context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [input], [0]));
        Assert.AreEqual("WRPRUNTIME1006", invalidated.Code);
    }

    [TestMethod]
    public async Task Logical_call_depth_is_not_changed_by_host_jit_inlining()
    {
        await using var context = new WarpRuntimeContext(LoadModule(), WarpBackendKind.CoreCLR,
            new WarpRuntimeOptions { MaximumCallDepth = 1 });
        WarpRuntimeFaultException fault = await Assert.ThrowsAsync<WarpRuntimeFaultException>(() =>
            context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [new uint[] { 5 }], [0]));
        Assert.AreEqual(WarpRuntimeFaultKind.CallDepth, fault.Kind);
        Assert.AreEqual(WarpRuntimeContextState.Faulted, context.State);
    }

    [TestMethod]
    public async Task Failed_buffer_admission_does_not_compile_or_taint_the_context()
    {
        await using var context = new WarpRuntimeContext(LoadModule(), WarpBackendKind.CoreCLR,
            new WarpRuntimeOptions { MaximumBufferBytes = 12 });
        WarpHostException error = await Assert.ThrowsAsync<WarpHostException>(() =>
            context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [new uint[2]], [0]));
        Assert.AreEqual("WRPRUNTIME1005", error.Code);
        Assert.AreEqual(0, context.JitStatistics.CompilationCount);
        uint[] output = await context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [new uint[1]], [5]);
        Assert.AreEqual(5u, output[0]);
        Assert.AreEqual(WarpRuntimeContextState.Ready, context.State);
    }

    [TestMethod]
    public async Task Precancelled_dispatch_does_not_compile_or_taint_context()
    {
        await using var context = new WarpRuntimeContext(LoadModule(), WarpBackendKind.CoreCLR);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => context.DispatchIntegerMapAsync(
            ManifestAssemblyFixture.MapEntryIdentity, [new uint[1]], [0], cancellation.Token));
        Assert.AreEqual(0, context.JitStatistics.CompilationCount);
        Assert.AreEqual(WarpRuntimeContextState.Ready, context.State);
    }

    [TestMethod]
    public async Task Async_disposal_drains_dispatches_and_is_idempotent_under_concurrency()
    {
        var context = new WarpRuntimeContext(LoadModule(), WarpBackendKind.CoreCLR);
        Task<uint[]> dispatch = context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [new uint[1_000_000]], [0]);
        Task[] disposals = Enumerable.Range(0, 8).Select(_ => context.DisposeAsync().AsTask()).ToArray();
        await Task.WhenAll(disposals);
        try
        {
            await dispatch;
        }
        catch (OperationCanceledException)
        {
        }

        Assert.AreEqual(WarpRuntimeContextState.Disposed, context.State);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => context.DispatchIntegerMapAsync(
            ManifestAssemblyFixture.MapEntryIdentity, [new uint[1]], [0]));
    }

    [TestMethod]
    public async Task Large_reduction_maps_parallel_workers_and_reduces_multiple_passes_exactly()
    {
        await using var context = new WarpRuntimeContext(LoadModule(), WarpBackendKind.CoreCLR);
        uint[] input = Enumerable.Range(0, 1_048_579).Select(index => unchecked((uint)index * 0xDEADBEEFu)).ToArray();
        uint expected = 0;
        foreach (uint value in input)
        {
            expected = unchecked(expected + TestKernels.ManifestReduction(value, 23));
        }

        uint actual = await context.DispatchUInt32ReductionAsync(ManifestAssemblyFixture.ReductionEntryIdentity, [input], [23]);
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public async Task Empty_map_and_reduction_observe_defined_identities()
    {
        await using var context = new WarpRuntimeContext(LoadModule(), WarpBackendKind.CoreCLR);
        uint[] output = await context.DispatchIntegerMapAsync(ManifestAssemblyFixture.MapEntryIdentity, [Array.Empty<uint>()], [0]);
        Assert.IsEmpty(output);
        Assert.AreEqual(0u, await context.DispatchUInt32ReductionAsync(ManifestAssemblyFixture.ReductionEntryIdentity, [Array.Empty<uint>()], [0]));
    }

    [TestMethod]
    public void Missing_native_provider_is_an_explicit_error_never_cpu_fallback()
    {
        WarpRuntimeModule module = LoadModule();
        foreach (WarpBackendKind backend in WarpBackendCatalog.Required.Where(backend => backend != WarpBackendKind.CoreCLR))
        {
            WarpHostException error = Assert.Throws<WarpHostException>(() => new WarpRuntimeContext(module, backend));
            Assert.AreEqual("WRPRUNTIME1003", error.Code);
        }
    }

    internal static WarpRuntimeModule LoadModule()
    {
        byte[] bytes = ManifestAssemblyFixture.ReadAssembly();
        return WarpRuntimeModule.Load(bytes, new WarpModuleTrust([Convert.ToHexString(SHA256.HashData(bytes))]));
    }
}
