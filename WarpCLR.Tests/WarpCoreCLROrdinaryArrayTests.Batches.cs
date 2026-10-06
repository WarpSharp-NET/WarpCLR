using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLROrdinaryArrayTests
{
    [TestMethod]
    public async Task BatchUsesOnlyCapturedStatesAndKeepsBindingAndStateUsesThroughPublication()
    {
        WarpLogicalMachineLayout layout = OrdinaryLayout();
        uint[] input = [0x80000001, 0x7FC00001], scalars = [0xFFFFFFFF];
        uint[] first = layout.CreateInitialState(32, 100), second = layout.CreateInitialState(32, 100);
        uint[] replacement = layout.CreateInitialState(32, 100), replacementBefore = (uint[])replacement.Clone();
        uint[][] callerStates = [first, second];
        uint[][] banks = [input, scalars, first, second];
        ConsistencyOwner[] owners = banks.Select(bank => new ConsistencyOwner([bank])).ToArray();
        int publications = 0;
        var hooks = new WarpCoreCLRWorkerTestHooks
        {
            BeforeBatchPublication = () =>
            {
                publications++;
                foreach (ConsistencyOwner owner in owners) { Assert.ThrowsExactly<InvalidOperationException>(owner.Hold); }
            },
        };
        WarpCoreCLRWorkerKernel compiled = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None, hooks).ConfigureAwait(false);
        await using var compiledOwner = compiled.ConfigureAwait(false);
        WarpCoreCLRWorkerLease lease = compiled.TryAcquireLease()!;
        await using var leaseOwner = lease.ConfigureAwait(false);
        WarpCoreCLRInputBinding binding = await lease.BindInputsAsync([input], scalars, CancellationToken.None).ConfigureAwait(false);
        await using var bindingOwner = binding.ConfigureAwait(false);
        using WarpCoreCLRWordTransaction.Lease paused = await WarpCoreCLRWordTransaction.AcquireAsync(first, [], CancellationToken.None).ConfigureAwait(false);
        Task executing = lease.ExecuteBatchAsync(binding, 0, callerStates, 32, 4096, CancellationToken.None);
        Assert.IsFalse(executing.IsCompleted);
        callerStates[0] = replacement;
        foreach (ConsistencyOwner owner in owners) { Assert.ThrowsExactly<InvalidOperationException>(owner.Hold); }
        paused.Dispose();
        await executing.ConfigureAwait(false);
        Assert.AreEqual(1, publications);
        Assert.AreEqual(input[0] ^ scalars[0], first[WarpLogicalMachineLayout.ResultOffset]);
        Assert.AreEqual(input[1] ^ scalars[0], second[WarpLogicalMachineLayout.ResultOffset]);
        CollectionAssert.AreEqual(replacementBefore, replacement);
        foreach (ConsistencyOwner owner in owners) { owner.Hold(); owner.Release(); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BatchRejectsAProtectedCapturedStateBeforeCopyOrTransport(bool quarantine)
    {
        WarpLogicalMachineLayout layout = OrdinaryLayout();
        WarpCoreCLRWorkerKernel compiled = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var compiledOwner = compiled.ConfigureAwait(false);
        WarpCoreCLRWorkerLease lease = compiled.TryAcquireLease()!;
        await using var leaseOwner = lease.ConfigureAwait(false);
        WarpCoreCLRInputBinding binding = await lease.BindInputsAsync([[0x80000001]], [0x7FC00001], CancellationToken.None).ConfigureAwait(false);
        await using var bindingOwner = binding.ConfigureAwait(false);
        uint[] state = layout.CreateInitialState(32, 100), before = (uint[])state.Clone();
        var owner = new ConsistencyOwner([state]);
        if (quarantine) { owner.Quarantine(); } else { owner.Hold(); }
        long reserved = WarpCoreCLRTransferAdmission.ReservedBytes;
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.ExecuteBatchAsync(binding, 0, [state], 32, 4096, CancellationToken.None)).ConfigureAwait(false);
        CollectionAssert.AreEqual(before, state);
        Assert.AreEqual(reserved, WarpCoreCLRTransferAdmission.ReservedBytes);
        Assert.IsFalse(lease.IsFaulted);
        // Failed admission released its binding use, so disposal can drain without a stranded row.
        await binding.DisposeAsync().ConfigureAwait(false);
        if (!quarantine) { owner.Release(); }
    }

    [TestMethod]
    public async Task ConcurrentBindingDisposalDrainsAnAdmittedBatchAndRejectsNewUses()
    {
        WarpLogicalMachineLayout layout = OrdinaryLayout();
        uint[] input = [0x80000001], scalars = [0x7FC00001];
        WarpCoreCLRInputBinding? binding = null;
        Task? firstDisposal = null, secondDisposal = null;
        var hooks = new WarpCoreCLRWorkerTestHooks
        {
            BeforeBatchPublication = () =>
            {
                Assert.IsNotNull(binding);
                firstDisposal = binding.DisposeAsync().AsTask();
                secondDisposal = binding.DisposeAsync().AsTask();
                Assert.AreSame(firstDisposal, secondDisposal);
                Assert.IsFalse(firstDisposal.IsCompleted);
                Assert.ThrowsExactly<InvalidOperationException>(() => binding.AcquireBatchUse(binding.Process));
            },
        };
        WarpCoreCLRWorkerKernel compiled = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None, hooks).ConfigureAwait(false);
        await using var compiledOwner = compiled.ConfigureAwait(false);
        WarpCoreCLRWorkerLease lease = compiled.TryAcquireLease()!;
        await using var leaseOwner = lease.ConfigureAwait(false);
        long beforeBinding = WarpCoreCLRTransferAdmission.ReservedBytes;
        binding = await lease.BindInputsAsync([input], scalars, CancellationToken.None).ConfigureAwait(false);
        await using var bindingOwner = binding.ConfigureAwait(false);
        uint[] state = layout.CreateInitialState(32, 100);
        await lease.ExecuteBatchAsync(binding, 0, [state], 32, 4096, CancellationToken.None).ConfigureAwait(false);
        Assert.IsNotNull(firstDisposal);
        Assert.IsNotNull(secondDisposal);
        await firstDisposal.ConfigureAwait(false);
        await secondDisposal.ConfigureAwait(false);
        Assert.AreEqual(input[0] ^ scalars[0], state[WarpLogicalMachineLayout.ResultOffset]);
        Assert.IsTrue(binding.IsDisposed);
        Assert.AreEqual(beforeBinding, WarpCoreCLRTransferAdmission.ReservedBytes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.ExecuteBatchAsync(binding, 0,
            [layout.CreateInitialState(32, 100)], 32, 4096, CancellationToken.None)).ConfigureAwait(false);
        await binding.DisposeAsync().ConfigureAwait(false);
    }
}
