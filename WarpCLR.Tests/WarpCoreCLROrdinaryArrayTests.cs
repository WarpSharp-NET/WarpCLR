using System.Diagnostics.CodeAnalysis;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

// Proposed consistency-only tests, UNCOMPILED/UNVALIDATED. No owner here grants Source permission.
[TestClass]
[DoNotParallelize]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest constructs this internal fixture through reflected discovery under DiscoverInternals.")]
internal sealed partial class WarpCoreCLROrdinaryArrayTests
{
    [TestMethod]
    public void InputBindingAndUseRejectUnknownIssuersBeforeInspectingOtherArguments()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => new WarpCoreCLRInputBinding(new object(), null!, 0, null!, null!, null!, null!));
        Assert.ThrowsExactly<InvalidOperationException>(() => new WarpCoreCLRInputBinding.BatchUse(new object(), null!));
    }

    [TestMethod]
    [DataRow(0, false)]
    [DataRow(1, false)]
    [DataRow(2, false)]
    [DataRow(3, false)]
    [DataRow(0, true)]
    [DataRow(1, true)]
    [DataRow(2, true)]
    [DataRow(3, true)]
    public async Task ExecuteRejectsEveryProtectedOriginalBankBeforeStartingACommand(int role, bool quarantine)
    {
        WarpLogicalMachineLayout layout = OrdinaryLayout();
        WarpCoreCLRWorkerKernel compiled = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var compiledOwner = compiled.ConfigureAwait(false);
        WarpCoreCLRWorkerLease lease = compiled.TryAcquireLease()!;
        await using var leaseOwner = lease.ConfigureAwait(false);
        uint[] input = [0x7FC00001], scalars = [0x80000001], state = layout.CreateInitialState(32, 100), arena = [0xDEADBEEF];
        uint[][] banks = [input, scalars, state, arena];
        uint[][] before = banks.Select(bank => (uint[])bank.Clone()).ToArray();
        var owner = new ConsistencyOwner([banks[role]]);
        if (quarantine) { owner.Quarantine(); } else { owner.Hold(); }
        long reserved = WarpCoreCLRTransferAdmission.ReservedBytes;
        WarpHostException error = await Assert.ThrowsAsync<WarpHostException>(() => lease.ExecuteManagedQuantumAsync(
            [input], scalars, 0, state, 32, 4096, arena, CancellationToken.None)).ConfigureAwait(false);
        Assert.IsInstanceOfType<InvalidOperationException>(error.InnerException);
        for (int index = 0; index < banks.Length; index++) { CollectionAssert.AreEqual(before[index], banks[index]); }
        Assert.IsFalse(lease.IsFaulted);
        Assert.AreEqual(reserved, WarpCoreCLRTransferAdmission.ReservedBytes);
        if (!quarantine)
        {
            owner.Release();
            await lease.ExecuteManagedQuantumAsync([input], scalars, 0, state, 32, 4096, arena, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(input[0] ^ scalars[0], state[WarpLogicalMachineLayout.ResultOffset]);
        }
    }

    [TestMethod]
    public async Task ExecuteCapturesInputsBeforeWaitingAndRetainsAllBanksThroughPublication()
    {
        WarpLogicalMachineLayout layout = OrdinaryLayout();
        uint[] input = [0xFFFFFFFF], scalars = [0x80000001], state = layout.CreateInitialState(32, 100), arena = [0xDEADBEEF];
        uint[] replacement = [0x7FC00001];
        uint[][] inputs = [input];
        uint[][] banks = [input, scalars, state, arena];
        ConsistencyOwner[] owners = banks.Select(bank => new ConsistencyOwner([bank])).ToArray();
        int publications = 0;
        var hooks = new WarpCoreCLRWorkerTestHooks
        {
            BeforeResultPublication = () =>
            {
                publications++;
                foreach (ConsistencyOwner owner in owners) { Assert.ThrowsExactly<InvalidOperationException>(owner.Hold); }
            },
        };
        WarpCoreCLRWorkerKernel compiled = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None, hooks).ConfigureAwait(false);
        await using var compiledOwner = compiled.ConfigureAwait(false);
        WarpCoreCLRWorkerLease lease = compiled.TryAcquireLease()!;
        await using var leaseOwner = lease.ConfigureAwait(false);
        using WarpCoreCLRWordTransaction.Lease paused = await WarpCoreCLRWordTransaction.AcquireAsync(state, arena, CancellationToken.None).ConfigureAwait(false);
        Task executing = lease.ExecuteManagedQuantumAsync(inputs, scalars, 0, state, 32, 4096, arena, CancellationToken.None);
        Assert.IsFalse(executing.IsCompleted);
        inputs[0] = replacement;
        foreach (ConsistencyOwner owner in owners) { Assert.ThrowsExactly<InvalidOperationException>(owner.Hold); }
        paused.Dispose();
        await executing.ConfigureAwait(false);
        Assert.AreEqual(1, publications);
        Assert.AreEqual(input[0] ^ scalars[0], state[WarpLogicalMachineLayout.ResultOffset]);
        Assert.AreEqual(0x7FC00001u, replacement[0]);
        Assert.AreEqual(0xDEADBEEFu, arena[0]);
        foreach (ConsistencyOwner owner in owners) { owner.Hold(); owner.Release(); }
    }

    private static WarpLogicalMachineLayout OrdinaryLayout() => new(new WarpControlFlowKernel("ordinary-original-arrays", 1, 1,
        [new WarpBasicBlock(0, [], [new(0, WarpIrOpCode.LoadInput), new(1, WarpIrOpCode.LoadScalar),
            new(2, WarpIrOpCode.ExclusiveOr, 0, 1)], new WarpReturnTerminator(2))]));

    private sealed class ConsistencyOwner
    {
        private readonly object owner = new();
        private readonly object authority = new();
        private readonly WarpOrdinaryArrayOwner enrolled;

        internal ConsistencyOwner(uint[][] banks) => enrolled = WarpOrdinaryArrayRegistry.EnrollOwner(owner, authority, banks);
        internal void Hold() => WarpOrdinaryArrayRegistry.Hold(enrolled, owner, authority);
        internal void Release() => WarpOrdinaryArrayRegistry.ReleaseHold(enrolled, owner, authority);
        internal void Quarantine() => WarpOrdinaryArrayRegistry.Quarantine(enrolled, owner, authority);
    }
}
