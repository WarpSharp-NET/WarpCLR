using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLROrdinaryArrayTests
{
    [TestMethod]
    [DataRow(0, false)]
    [DataRow(1, false)]
    [DataRow(0, true)]
    [DataRow(1, true)]
    public async Task BindRejectsProtectedInputOrScalarBeforeHashingAndTransport(int role, bool quarantine)
    {
        WarpCoreCLRWorkerKernel compiled = await WarpCoreCLRWorkerKernel.CompileAsync(OrdinaryLayout(), new(), CancellationToken.None).ConfigureAwait(false);
        await using var compiledOwner = compiled.ConfigureAwait(false);
        WarpCoreCLRWorkerLease lease = compiled.TryAcquireLease()!;
        await using var leaseOwner = lease.ConfigureAwait(false);
        uint[] input = [0x7FC00001], scalars = [0xFFFFFFFF];
        uint[][] banks = [input, scalars];
        var owner = new ConsistencyOwner([banks[role]]);
        if (quarantine) { owner.Quarantine(); } else { owner.Hold(); }
        long reserved = WarpCoreCLRTransferAdmission.ReservedBytes;
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.BindInputsAsync([input], scalars, CancellationToken.None)).ConfigureAwait(false);
        Assert.IsFalse(lease.IsFaulted);
        Assert.AreEqual(reserved, WarpCoreCLRTransferAdmission.ReservedBytes);
        Assert.AreEqual(0x7FC00001u, input[0]);
        Assert.AreEqual(0xFFFFFFFFu, scalars[0]);
        if (!quarantine) { owner.Release(); }
    }

    [TestMethod]
    [DataRow(0, false)]
    [DataRow(1, false)]
    [DataRow(0, true)]
    [DataRow(1, true)]
    public async Task BatchRetainsOriginalBindingIdentityEvenWhenEqualCopiesAndDigestsExist(int role, bool quarantine)
    {
        WarpLogicalMachineLayout layout = OrdinaryLayout();
        uint[] input = [0x80000001], scalars = [0x7FC00001];
        uint[] equalInput = (uint[])input.Clone(), equalScalars = (uint[])scalars.Clone();
        uint[][] callerInputs = [input];
        var active = new ConsistencyOwner([role == 0 ? input : scalars]);
        int publications = 0;
        var hooks = new WarpCoreCLRWorkerTestHooks
        {
            BeforeInputBindingPublication = () =>
            {
                if (publications++ == 0)
                {
                    Assert.ThrowsExactly<InvalidOperationException>(active.Hold);
                    callerInputs[0] = equalInput;
                }
            },
        };
        WarpCoreCLRWorkerKernel compiled = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None, hooks).ConfigureAwait(false);
        await using var compiledOwner = compiled.ConfigureAwait(false);
        WarpCoreCLRWorkerLease lease = compiled.TryAcquireLease()!;
        await using var leaseOwner = lease.ConfigureAwait(false);
        WarpCoreCLRInputBinding binding = await lease.BindInputsAsync(callerInputs, scalars, CancellationToken.None).ConfigureAwait(false);
        await using var bindingOwner = binding.ConfigureAwait(false);
        // The first bind has released its ordinary use; the retained provenance itself is not an active use.
        active.Hold();
        active.Release();
        if (quarantine) { active.Quarantine(); } else { active.Hold(); }
        uint[] state = layout.CreateInitialState(32, 100), before = (uint[])state.Clone();
        long reserved = WarpCoreCLRTransferAdmission.ReservedBytes;
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.ExecuteBatchAsync(binding, 0, [state], 32, 4096, CancellationToken.None)).ConfigureAwait(false);
        CollectionAssert.AreEqual(before, state);
        Assert.IsFalse(lease.IsFaulted);
        Assert.AreEqual(reserved, WarpCoreCLRTransferAdmission.ReservedBytes);
        WarpCoreCLRInputBinding independent = await lease.BindInputsAsync([equalInput], equalScalars, CancellationToken.None).ConfigureAwait(false);
        await using var independentOwner = independent.ConfigureAwait(false);
        await lease.ExecuteBatchAsync(independent, 0, [state], 32, 4096, CancellationToken.None).ConfigureAwait(false);
        Assert.AreEqual(equalInput[0] ^ equalScalars[0], state[WarpLogicalMachineLayout.ResultOffset]);
        if (!quarantine) { active.Release(); }
    }

    [TestMethod]
    public async Task BindingDigestCannotBeMutatedThroughItsReadOnlyReceipt()
    {
        WarpLogicalMachineLayout layout = OrdinaryLayout();
        WarpCoreCLRWorkerKernel compiled = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var compiledOwner = compiled.ConfigureAwait(false);
        WarpCoreCLRWorkerLease lease = compiled.TryAcquireLease()!;
        await using var leaseOwner = lease.ConfigureAwait(false);
        WarpCoreCLRInputBinding binding = await lease.BindInputsAsync([[0x7FC00001]], [0xFFFFFFFF], CancellationToken.None).ConfigureAwait(false);
        await using var bindingOwner = binding.ConfigureAwait(false);
        byte[] original = binding.Identity, callerCopy = binding.Identity;
        callerCopy[0] ^= 1;
        CollectionAssert.AreEqual(original, binding.Identity);
        uint[] state = layout.CreateInitialState(32, 100);
        await lease.ExecuteBatchAsync(binding, 0, [state], 32, 4096, CancellationToken.None).ConfigureAwait(false);
        Assert.AreEqual(0x803FFFFEu, state[WarpLogicalMachineLayout.ResultOffset]);
    }
}
