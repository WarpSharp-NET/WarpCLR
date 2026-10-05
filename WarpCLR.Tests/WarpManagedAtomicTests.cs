using System.Collections.Concurrent;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Runtime.Host.Native;

namespace WarpCLR.Tests;

[TestClass]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest creates this fixture through discovery.")]
internal sealed class WarpManagedAtomicTests
{
    [TestMethod]
    public void CompiledAtomicOperationsPreserveWordBitsAndSpecifiedReturnValues()
    {
        foreach (WarpLogicalMachineLayout layout in WarpManagedAtomicKernels.Create32())
        {
            string operation = layout.Kernel.Name[(layout.Kernel.Name.LastIndexOf('/') + 1)..];
            uint initial = 0xFFFFFFFF;
            uint replacement = 0x7FC01234;
            uint[][] arguments = Enumerable.Range(0, layout.Kernel.InputBufferCount)
                .Select(index => new[] { index == 0 ? 1u : index == 1 && string.Equals(operation, "compare-exchange", StringComparison.Ordinal) ? initial : replacement }).ToArray();
            uint[] arena = [17, initial, 19];
            uint value = Execute(layout, arguments, arena);
            uint expected = operation switch
            {
                "store-sc" or "store-release" => replacement,
                "add" => unchecked(initial + replacement),
                "fence-sc" => 0,
                _ => initial,
            };
            uint written = operation switch
            {
                "compare-exchange" or "exchange" or "store-sc" or "store-release" => replacement,
                "add" => unchecked(initial + replacement),
                _ => initial,
            };
            Assert.AreEqual(expected, value, operation);
            CollectionAssert.AreEqual(new uint[] { 17, written, 19 }, arena, operation);
        }

        WarpLogicalMachineLayout compare = Layout("compare-exchange");
        uint[] failed = [0xDEADBEEF];
        Assert.AreEqual(0xDEADBEEFu, Execute(compare, [[0], [99], [7]], failed));
        Assert.AreEqual(0xDEADBEEFu, failed[0]);
    }

    [TestMethod]
    public void OutOfRangeAtomicsFaultBeforeTouchingStorage()
    {
        foreach (WarpLogicalMachineLayout layout in WarpManagedAtomicKernels.Create32()
            .Where(item => !item.Kernel.Name.EndsWith("/fence-sc", StringComparison.Ordinal)))
        {
            CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
            uint[] state = layout.CreateInitialState(1, 100);
            uint[][] inputs = Enumerable.Range(0, layout.Kernel.InputBufferCount)
                .Select(index => new[] { index == 0 ? uint.MaxValue : 9u }).ToArray();
            uint[] arena = [0xFFFFFFFF];
            core.ExecuteManagedQuantum(inputs, [], 0, state, 1, 100, arena);
            Assert.AreEqual(WarpLogicalMachineLayout.Faulted, state[WarpLogicalMachineLayout.StatusOffset]);
            Assert.AreEqual(WarpLogicalMachineLayout.ManagedMemoryBoundsFault, state[WarpLogicalMachineLayout.FaultKindOffset]);
            Assert.AreEqual(0u, state[WarpLogicalMachineLayout.ResultOffset]);
            Assert.AreEqual(0xFFFFFFFFu, arena[0]);
        }
    }

    [TestMethod]
    public async Task SharedCompiledAtomicAddsHaveNoLostUpdatesOrDuplicateResults()
    {
        WarpLogicalMachineLayout layout = Layout("add");
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        uint[] arena = [0];
        var returned = new ConcurrentBag<uint>();
        Task[] tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (int index = 0; index < 512; index++)
            {
                uint[] state = layout.CreateInitialState(1, 100);
                compiled.ExecuteManagedQuantum([[0], [1]], [], 0, state, 1, 100, arena);
                Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
                returned.Add(state[WarpLogicalMachineLayout.ResultOffset]);
            }
        })).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        Assert.AreEqual(2048u, arena[0]);
        CollectionAssert.AreEqual(Enumerable.Range(1, 2048).Select(item => (uint)item).ToArray(), returned.Order().ToArray());
    }

    [TestMethod]
    public async Task GeneratedReleaseAndAcquirePublishOrdinaryWordWrites()
    {
        WarpLogicalMachineLayout releaseLayout = Layout("store-release");
        WarpLogicalMachineLayout acquireLayout = Layout("load-acquire");
        CoreCLRResumableKernel release = CoreCLRResumableKernel.Compile(releaseLayout);
        CoreCLRResumableKernel acquire = CoreCLRResumableKernel.Compile(acquireLayout);
        uint[] arena = [0, 0];
        Task publisher = Task.Run(() =>
        {
            arena[1] = 0x1234ABCD;
            uint[] state = releaseLayout.CreateInitialState(1, 100);
            release.ExecuteManagedQuantum([[0], [1]], [], 0, state, 1, 100, arena);
        });
        uint observed = 0;
        for (int attempt = 0; attempt < 100000 && observed == 0; attempt++)
        {
            uint[] state = acquireLayout.CreateInitialState(1, 100);
            acquire.ExecuteManagedQuantum([[0]], [], 0, state, 1, 100, arena);
            observed = state[WarpLogicalMachineLayout.ResultOffset];
            if (observed == 0)
            {
                await Task.Yield();
            }
        }

        Assert.AreEqual(1u, observed);
        Assert.AreEqual(0x1234ABCDu, arena[1]);
        await publisher.ConfigureAwait(false);
    }

    [TestMethod]
    public async Task SequentialPtxOperationsRejectTargetsWithoutSystemAcquireAndFenceInstructions()
    {
        var target = new WarpNativeTarget(WarpBackendKind.NVPTX, "sm_60", "admission-test", "admission-test", 1024, uint.MaxValue, 1UL << 30);
        var toolchain = new WarpNativeToolchain(new WarpNativeToolchainOptions { LlvmAssembler = "/missing/never-invoked" });
        foreach (WarpLogicalMachineLayout layout in WarpManagedAtomicKernels.Create32())
        {
            WarpHostException error = await Assert.ThrowsExactlyAsync<WarpHostException>(() => toolchain.CompileMachineAsync(layout, target)).ConfigureAwait(false);
            Assert.AreEqual("WRPNATIVE1003", error.Code, StringComparer.Ordinal);
        }
    }

    private static WarpLogicalMachineLayout Layout(string operation) => WarpManagedAtomicKernels.Create32()
        .First(item => item.Kernel.Name.EndsWith("/" + operation, StringComparison.Ordinal));

    private static uint Execute(WarpLogicalMachineLayout layout, uint[][] inputs, uint[] arena)
    {
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(1, 100);
        core.ExecuteManagedQuantum(inputs, [], 0, state, 1, 100, arena);
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        return state[WarpLogicalMachineLayout.ResultOffset];
    }
}
