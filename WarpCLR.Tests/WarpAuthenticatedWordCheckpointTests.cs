using System.Diagnostics.CodeAnalysis;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest constructs this internal fixture through reflected discovery under DiscoverInternals.")]
internal sealed class WarpAuthenticatedWordCheckpointTests
{
    [TestMethod]
    public void CallerConstructedCheckpointRequiresRuntimeSecretBeforeInspectingAnything() =>
        Assert.ThrowsExactly<InvalidOperationException>(() => new WarpCoreCLRWorkerProcess.WordCheckpoint(null!, [], [], [], new object()));

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ActualChildCommitRequiresLatestExactModuleAndCompleteStorage(bool largeQuantum)
    {
        WarpLogicalMachineLayout layout = Layout();
        WarpCoreCLRWorkerKernel kernel = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var ownedKernel = kernel.ConfigureAwait(false);
        WarpCoreCLRWorkerLease lease = kernel.TryAcquireLease()!;
        await using var ownedLease = lease.ConfigureAwait(false);
        uint[] state = layout.CreateInitialState(8, 10000);
        uint[] arena = [0, 1, 0x80000000, uint.MaxValue];
        Assert.ThrowsExactly<InvalidOperationException>(() => lease.RequireCommittedWordCheckpoint(state, arena));
        WarpCoreCLRWorkerProcess.WordCheckpoint? previous = null;
        int quantum = largeQuantum ? 4096 : layout.MaximumBlockCost;
        while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable)
        {
            await lease.ExecuteManagedQuantumAsync([[17]], [], 0, state, 8, quantum, arena).ConfigureAwait(false);
            WarpCoreCLRWorkerProcess.WordCheckpoint checkpoint = lease.RequireCommittedWordCheckpoint(state, arena);
            Assert.AreEqual(lease.ProcessId, checkpoint.ProcessId);
            Assert.AreEqual(lease.CompiledModule, checkpoint.Module);
            Assert.AreNotEqual(Environment.ProcessId, checkpoint.ProcessId);
            Assert.AreEqual(WarpIrHash.Compute(layout.Kernel), checkpoint.IrHash, StringComparer.Ordinal);
            Assert.IsGreaterThanOrEqualTo(2UL, checkpoint.Sequence);
            Assert.IsGreaterThan(0UL, checkpoint.Ordinal);
            Assert.HasCount(64, checkpoint.RequestHash);
            Assert.HasCount(64, checkpoint.ResponseHash);
            lease.ValidateCommittedWordCheckpoint(checkpoint, state, arena);
            Assert.ThrowsExactly<InvalidOperationException>(() => lease.RequireCommittedWordCheckpoint((uint[])state.Clone(), arena));
            Assert.ThrowsExactly<InvalidOperationException>(() => lease.RequireCommittedWordCheckpoint(state, (uint[])arena.Clone()));
            arena[2] ^= 1;
            uint[] changedArena = (uint[])arena.Clone();
            Assert.ThrowsExactly<InvalidOperationException>(() => lease.RequireCommittedWordCheckpoint(state, arena));
            CollectionAssert.AreEqual(changedArena, arena);
            arena[2] ^= 1;
            if (previous is not null)
            {
                Assert.IsGreaterThan(previous.Ordinal, checkpoint.Ordinal);
                Assert.AreEqual(previous.Sequence + 1, checkpoint.Sequence);
                Assert.ThrowsExactly<InvalidOperationException>(() => lease.ValidateCommittedWordCheckpoint(previous, state, arena));
            }
            previous = checkpoint;
        }
        Assert.AreEqual(52u, state[layout.GetResultWordOffset(0, 8)]);
        WarpCoreCLRWorkerProcess.WordCheckpoint last = previous!;
        await lease.ExecuteManagedQuantumAsync([[17]], [], 0, state, 8, quantum, arena).ConfigureAwait(false);
        Assert.ThrowsExactly<InvalidOperationException>(() => lease.ValidateCommittedWordCheckpoint(last, state, arena));
        Assert.AreNotSame(last, lease.RequireCommittedWordCheckpoint(state, arena));
    }

    [TestMethod]
    public async Task IdenticalIrAndWordBytesCannotTransferAReceiptToAnotherActualChildModule()
    {
        WarpLogicalMachineLayout layout = Layout();
        WarpCoreCLRWorkerKernel first = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var firstOwner = first.ConfigureAwait(false);
        WarpCoreCLRWorkerKernel second = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var secondOwner = second.ConfigureAwait(false);
        WarpCoreCLRWorkerLease a = first.TryAcquireLease()!;
        await using var aOwner = a.ConfigureAwait(false);
        WarpCoreCLRWorkerLease b = second.TryAcquireLease()!;
        await using var bOwner = b.ConfigureAwait(false);
        Assert.AreNotEqual(a.ProcessId, b.ProcessId);
        Assert.AreNotEqual(a.CompiledModule, b.CompiledModule);
        uint[] state = layout.CreateInitialState(8, 10000);
        uint[] arena = [1, 2, 3];
        await a.ExecuteManagedQuantumAsync([[17]], [], 0, state, 8, 4096, arena).ConfigureAwait(false);
        WarpCoreCLRWorkerProcess.WordCheckpoint receipt = a.RequireCommittedWordCheckpoint(state, arena);
        Assert.ThrowsExactly<InvalidOperationException>(() => b.RequireCommittedWordCheckpoint(state, arena));
        Assert.ThrowsExactly<InvalidOperationException>(() => b.ValidateCommittedWordCheckpoint(receipt, state, arena));
        a.ValidateCommittedWordCheckpoint(receipt, state, arena);
        await b.ExecuteManagedQuantumAsync([[17]], [], 0, state, 8, 4096, arena).ConfigureAwait(false);
        Assert.ThrowsExactly<InvalidOperationException>(() => a.ValidateCommittedWordCheckpoint(receipt, state, arena));
        Assert.AreEqual(b.CompiledModule, b.RequireCommittedWordCheckpoint(state, arena).Module);
    }

    private static WarpLogicalMachineLayout Layout() => new(new WarpIntegerMapVerifier().Verify(
        new WarpIntegerMapRequest(typeof(WarpAuthenticatedWordCheckpointKernels).GetMethod(nameof(WarpAuthenticatedWordCheckpointKernels.Calculate))!, 1)).ControlFlow);

}

