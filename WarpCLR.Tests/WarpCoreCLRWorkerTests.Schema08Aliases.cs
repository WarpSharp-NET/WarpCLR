using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLRWorkerTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ActualChildAliasesPreserveOriginalPrefixAndCompleteBankAtBothQuanta(bool callSource)
    {
        WarpLogicalMachineLayout layout = WarpFilterAliasHookKernels.Create(callSource);
        WarpCoreCLRWorkerKernel child = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var owner = child.ConfigureAwait(false);
        WarpCoreCLRWorkerLease lease = child.TryAcquireLease()!;
        await using var leaseOwner = lease.ConfigureAwait(false);
        AssertActualSchemaChild(lease);
        int depth = callSource ? 2 : 1;
        foreach (uint value in new uint[] { 0, uint.MaxValue, 0x7FA12345 })
        {
            uint[] initial = layout.CreateInitialState(depth, callSource ? 4 : 3);
            layout.SetSourceBoundaryMode(initial, enabled: true);
            uint[] arena = [0xCAFE, 0xFEDCBA98];
            uint[] small = await RunSchemaChildAsync(lease, initial, [[value]], 0, depth, layout.MaximumBlockCost, arena).ConfigureAwait(false);
            uint[] large = await RunSchemaChildAsync(lease, initial, [[value]], 0, depth, 4096, arena).ConfigureAwait(false);
            CollectionAssert.AreEqual(small, large); CollectionAssert.AreEqual(new uint[] { 0xCAFE, 0xFEDCBA98 }, arena);
            Assert.AreEqual(WarpLogicalMachineLayout.Completed, small[0]);
            Assert.AreEqual(unchecked(value + 1), small[WarpLogicalMachineLayout.ResultOffset]);
            Assert.AreEqual(WarpFilterAliasHookKernels.OriginalTail, small[WarpLogicalMachineLayout.ResultHighOffset]);
            Assert.AreEqual(WarpFilterAliasHookKernels.PrefixSentinel, small[WarpFilterAliasHookKernels.PrefixShadowResult]);
            Assert.AreEqual(WarpFilterAliasHookKernels.EvaluationSentinel, small[WarpFilterAliasHookKernels.EvaluationResult]);
            Assert.AreEqual(initial[WarpLogicalMachineLayout.OwnerContextOffset], small[WarpFilterAliasHookKernels.OwnerContextResult]);
            Assert.AreEqual(1u, small[WarpFilterAliasHookKernels.OwnerDepthResult]);
            Assert.AreEqual(1u, small[WarpFilterAliasHookKernels.OwnerActivationResult]);
            Assert.AreEqual(0u, small[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
        }
    }

    [TestMethod]
    [DataRow(0u, 1u)]
    [DataRow(3u, 1u)]
    [DataRow(1u, 0u)]
    public async Task ActualChildMalformedReturnedAliasNeverCommitsParentWords(uint ownerDepth, uint activation)
    {
        WarpLogicalMachineLayout layout = WarpFilterAliasHookKernels.Create(ownerDepth: ownerDepth, ownerActivation: activation);
        WarpCoreCLRWorkerKernel child = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var owner = child.ConfigureAwait(false);
        WarpCoreCLRWorkerLease lease = child.TryAcquireLease()!;
        await using var leaseOwner = lease.ConfigureAwait(false);
        uint[] state = layout.CreateInitialState(1, 3), before = (uint[])state.Clone(), arena = [0xCAFE];
        await Assert.ThrowsAsync<WarpHostException>(() => lease.ExecuteManagedQuantumAsync([[41]], [], 0,
            state, 1, 4096, arena, CancellationToken.None)).ConfigureAwait(false);
        CollectionAssert.AreEqual(before, state); CollectionAssert.AreEqual(new uint[] { 0xCAFE }, arena);
        Assert.IsTrue(lease.IsFaulted); AssertTerminated(lease.ProcessId);
        await Assert.ThrowsAsync<WarpHostException>(() => WarpCoreCLRWordTransaction.AcquireAsync(before, arena, CancellationToken.None)).ConfigureAwait(false);
    }
}
