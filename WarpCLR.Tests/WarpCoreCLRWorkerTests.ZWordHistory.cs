using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLRWorkerTests
{
    [TestMethod]
    [DataRow(false, 0u)]
    [DataRow(true, 0u)]
    [DataRow(true, 0x13579BDFu)]
    public async Task ActualOrdinaryWordHistoryPreservesEmptyZeroAndNonzeroCarriers(bool hasScalar, uint carrier)
    {
        var kernel = new WarpControlFlowKernel("ordinary-history-carrier", 1, hasScalar ? 1 : 0,
            [new WarpBasicBlock(0, [], [new(0, WarpIrOpCode.Constant, immediate: 0xC0FFEEu)], new WarpReturnTerminator(0))]);
        var layout = new WarpLogicalMachineLayout(kernel);
        WarpCoreCLRWorkerKernel compiled = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var kernelOwner = compiled.ConfigureAwait(false);
        WarpCoreCLRWorkerLease lease = compiled.TryAcquireLease()!;
        await using var leaseOwner = lease.ConfigureAwait(false);
        uint[][] inputs = [[17]];
        uint[] scalars = hasScalar ? [carrier] : [];
        uint[] state = layout.CreateInitialState(4, 100), arena = new uint[7];
        uint[] before = (uint[])state.Clone();
        await lease.ExecuteManagedQuantumAsync(inputs, scalars, 0, state, 4, 4096, arena, CancellationToken.None).ConfigureAwait(false);
        WarpCoreCLRWorkerProcess.WordCheckpoint checkpoint = lease.RequireCommittedWordCheckpoint(state, arena);
        checkpoint.ValidateHistoricalStorage(inputs, scalars, state, arena);
        WarpCoreCLRWorkerWords.Invocation request = checkpoint.CopyHistoricalRequest();
        var result = checkpoint.CopyHistoricalResult();
        CollectionAssert.AreEqual(scalars, request.Scalars);
        CollectionAssert.AreEqual(before, request.State);
        CollectionAssert.AreEqual(inputs[0], request.Inputs[0]);
        CollectionAssert.AreEqual(state, result.State);
        CollectionAssert.AreEqual(arena, result.Arena);
        Assert.AreEqual(0xC0FFEEu, result.State[WarpLogicalMachineLayout.ResultOffset]);
        RejectClonedHistoryStorage(checkpoint, inputs, scalars, state, arena);
        uint[] original = inputs[0]; inputs[0] = (uint[])original.Clone();
        Assert.ThrowsExactly<InvalidOperationException>(() => checkpoint.ValidateHistoricalStorage(inputs, scalars, state, arena));
        inputs[0] = original;
        uint[] wrongCarrier = hasScalar ? (carrier == 0 ? [0x13579BDFu] : [0]) : [0];
        Assert.ThrowsExactly<InvalidOperationException>(() => checkpoint.ValidateReadOnlyIdentity(WarpCoreCLRReadOnlyWordIdentity.FromArguments(inputs, wrongCarrier)));
        state[^1] ^= 1; arena[^1] ^= 1;
        Assert.ThrowsExactly<InvalidOperationException>(() => lease.ValidateCommittedWordCheckpoint(checkpoint, state, arena));
        checkpoint.ValidateHistoricalStorage(inputs, scalars, state, arena);
        CollectionAssert.AreEqual(result.State, checkpoint.CopyHistoricalResult().State);
        CollectionAssert.AreEqual(result.Arena, checkpoint.CopyHistoricalResult().Arena);
        request.State[^1] ^= 1; result.State[^1] ^= 1;
        CollectionAssert.AreEqual(before, checkpoint.CopyHistoricalRequest().State);
        Assert.AreNotEqual(result.State[^1], checkpoint.CopyHistoricalResult().State[^1]);
    }

    [TestMethod]
    public async Task OrdinaryHistoryRetainsTheActualAdmittedBankWithoutChangingOuterRebindingBehavior()
    {
        uint[][] inputs = [[17]];
        uint[] original = inputs[0];
        var hooks = new WarpCoreCLRWorkerTestHooks { BeforeResultPublication = () => inputs[0] = [999] };
        var kernel = new WarpControlFlowKernel("ordinary-history-rebound-input", 1, 0,
            [new WarpBasicBlock(0, [], [new(0, WarpIrOpCode.LoadInput)], new WarpReturnTerminator(0))]);
        var layout = new WarpLogicalMachineLayout(kernel);
        WarpCoreCLRWorkerKernel compiled = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None, hooks).ConfigureAwait(false);
        await using var kernelOwner = compiled.ConfigureAwait(false);
        WarpCoreCLRWorkerLease lease = compiled.TryAcquireLease()!;
        await using var leaseOwner = lease.ConfigureAwait(false);
        uint[] state = layout.CreateInitialState(4, 100), arena = new uint[7], scalars = [];
        await lease.ExecuteManagedQuantumAsync(inputs, scalars, 0, state, 4, 4096, arena, CancellationToken.None).ConfigureAwait(false);
        WarpCoreCLRWorkerProcess.WordCheckpoint checkpoint = lease.RequireCommittedWordCheckpoint(state, arena);
        Assert.AreEqual(17u, state[WarpLogicalMachineLayout.ResultOffset]);
        Assert.AreEqual(999u, inputs[0][0]);
        CollectionAssert.AreEqual(original, checkpoint.CopyHistoricalRequest().Inputs[0]);
        Assert.ThrowsExactly<InvalidOperationException>(() => checkpoint.ValidateHistoricalStorage(inputs, scalars, state, arena));
        inputs[0] = original;
        checkpoint.ValidateHistoricalStorage(inputs, scalars, state, arena);
    }

    private static void RejectClonedHistoryStorage(WarpCoreCLRWorkerProcess.WordCheckpoint checkpoint,
        uint[][] inputs, uint[] scalars, uint[] state, uint[] arena)
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => checkpoint.ValidateHistoricalStorage((uint[][])inputs.Clone(), scalars, state, arena));
        Assert.ThrowsExactly<InvalidOperationException>(() => checkpoint.ValidateHistoricalStorage(inputs, (uint[])scalars.Clone(), state, arena));
        Assert.ThrowsExactly<InvalidOperationException>(() => checkpoint.ValidateHistoricalStorage(inputs, scalars, (uint[])state.Clone(), arena));
        Assert.ThrowsExactly<InvalidOperationException>(() => checkpoint.ValidateHistoricalStorage(inputs, scalars, state, (uint[])arena.Clone()));
    }
}
