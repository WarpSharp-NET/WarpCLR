using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLRWorkerTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ActualChildLogicalWorkerUsesExactInt32InvocationAtEveryDepth(bool nested)
    {
        WarpLogicalMachineLayout layout = nested ? WarpLogicalWorkerHookKernels.CreateNested() : WarpLogicalWorkerHookKernels.CreateDirect();
        WarpCoreCLRWorkerKernel child = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var owner = child.ConfigureAwait(false);
        WarpCoreCLRWorkerLease lease = child.TryAcquireLease()!;
        await using var leaseOwner = lease.ConfigureAwait(false);
        AssertActualSchemaChild(lease);
        foreach (int worker in new[] { 0, 7, int.MaxValue })
        {
            uint[] initial = layout.CreateInitialState(2, 10);
            uint[] small = await RunSchemaChildAsync(lease, initial, [], worker, 2, layout.MaximumBlockCost, []).ConfigureAwait(false);
            uint[] large = await RunSchemaChildAsync(lease, initial, [], worker, 2, 4096, []).ConfigureAwait(false);
            CollectionAssert.AreEqual(small, large); Assert.AreEqual(WarpLogicalMachineLayout.Completed, small[0]);
            Assert.AreEqual((uint)worker, small[WarpLogicalMachineLayout.ResultOffset]);
            if (nested) { Assert.AreEqual(0x80000000u, small[WarpLogicalMachineLayout.ResultHighOffset]); }
            Assert.AreEqual(nested ? 8u : 9u, small[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
        }
    }

    [TestMethod]
    public async Task ActualChildInputIndexAndLogicalWorkerShareTheSameInvocationIdentity()
    {
        WarpLogicalMachineLayout layout = WarpLogicalWorkerHookKernels.CreateInputBinding();
        WarpCoreCLRWorkerKernel child = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var owner = child.ConfigureAwait(false);
        WarpCoreCLRWorkerLease lease = child.TryAcquireLease()!;
        await using var leaseOwner = lease.ConfigureAwait(false);
        uint[][] inputs = [[0x7FA12345, uint.MaxValue, 0x80000000, 17]];
        for (int worker = 0; worker < inputs[0].Length; worker++)
        foreach (int quantum in new[] { layout.MaximumBlockCost, 4096 })
        {
            uint[] state = await RunSchemaChildAsync(lease, layout.CreateInitialState(1, 10), inputs, worker, 1, quantum, []).ConfigureAwait(false);
            Assert.AreEqual((uint)worker, state[WarpLogicalMachineLayout.ResultOffset]);
            Assert.AreEqual(inputs[0][worker], state[WarpLogicalMachineLayout.ResultHighOffset]);
            Assert.AreEqual((uint)worker ^ inputs[0][worker], state[layout.GetResultWordOffset(2, 1)]);
        }
    }

    [TestMethod]
    public async Task ActualChildNegativeLogicalWorkerNeverReturnsOrCommitsWords()
    {
        WarpLogicalMachineLayout layout = WarpLogicalWorkerHookKernels.CreateDirect();
        WarpCoreCLRWorkerKernel child = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var owner = child.ConfigureAwait(false);
        WarpCoreCLRWorkerLease lease = child.TryAcquireLease()!;
        await using var leaseOwner = lease.ConfigureAwait(false);
        uint[] state = layout.CreateInitialState(1, 10), before = (uint[])state.Clone(), arena = [0xCAFE];
        await Assert.ThrowsAsync<WarpHostException>(() => lease.ExecuteManagedQuantumAsync([], [], -1,
            state, 1, 4096, arena, CancellationToken.None)).ConfigureAwait(false);
        CollectionAssert.AreEqual(before, state); CollectionAssert.AreEqual(new uint[] { 0xCAFE }, arena);
        Assert.IsTrue(lease.IsFaulted); AssertTerminated(lease.ProcessId);
    }
}
