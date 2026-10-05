using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLRWorkerTests
{
    [TestMethod]
    public async Task PrivilegedRuntimeStateWordCilUsesTheExactTransferredStateBank()
    {
        WarpLogicalMachineLayout layout = WarpWordStateServiceLowerer.Lower(typeof(WarpCoreCLRWorkerStateKernels)
            .GetMethod(nameof(WarpCoreCLRWorkerStateKernels.StateWords))!);
        WarpCoreCLRWorkerKernel compiled = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var owner = compiled.ConfigureAwait(false);
        WarpCoreCLRWorkerLease lease = compiled.TryAcquireLease()!;
        await using var leaseOwner = lease.ConfigureAwait(false);
        foreach (int quantum in new[] { layout.MaximumBlockCost, 4096 })
        {
            uint[] state = layout.CreateInitialState(32, 10);
            state[128] = 0x80000001;
            while (state[0] == 0)
            { await lease.ExecuteManagedQuantumAsync([[128], [0xDEADBEEF]], [], 0, state, 32, quantum, [], CancellationToken.None).ConfigureAwait(false); }
            Assert.AreEqual(0x80000001u ^ 0xDEADBEEFu, state[128]);
            Assert.AreEqual(unchecked(state[128] + (uint)state.Length), state[WarpLogicalMachineLayout.ResultOffset]);
            Assert.AreEqual(10u, state[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
        }
    }

    [TestMethod]
    public async Task SharedArenaTransactionsAcrossCompiledChildModulesPreserveEveryCommittedWrite()
    {
        WarpLogicalMachineLayout layout = WarpWordArenaServiceLowerer.Lower(typeof(WarpCoreCLRWorkerStateKernels)
            .GetMethod(nameof(WarpCoreCLRWorkerStateKernels.Increment))!);
        WarpCoreCLRWorkerKernel first = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var firstOwner = first.ConfigureAwait(false);
        WarpCoreCLRWorkerKernel second = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var secondOwner = second.ConfigureAwait(false);
        WarpCoreCLRWorkerLease left = first.TryAcquireLease()!, right = second.TryAcquireLease()!;
        await using var leftOwner = left.ConfigureAwait(false);
        await using var rightOwner = right.ConfigureAwait(false);
        uint[] arena = [0];
        Task[] work = Enumerable.Range(0, 64).Select(index => IncrementAsync(index % 2 == 0 ? left : right, arena)).ToArray();
        await Task.WhenAll(work).ConfigureAwait(false);
        Assert.AreEqual(64u, arena[0]); Assert.AreNotEqual(left.ProcessId, right.ProcessId);
    }

    private static async Task IncrementAsync(WarpCoreCLRWorkerLease lease, uint[] arena)
    {
        uint[] state = lease.Layout.CreateInitialState(32, 1000);
        while (state[0] == 0)
        { await lease.ExecuteManagedQuantumAsync(ServiceInputs(lease.Layout), [], 0, state, 32, 4096, arena, CancellationToken.None).ConfigureAwait(false); }
    }

    private static uint[][] ServiceInputs(WarpLogicalMachineLayout layout) =>
        Enumerable.Range(0, layout.Kernel.InputBufferCount).Select(_ => new uint[1]).ToArray();
}
