using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpCompiledSourceSchedulerTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CompletionIssuerRejectsWrongSecretBeforeCandidateBanks(bool corruptDescriptor)
    {
        WarpCompiledSourceContext context = Create(nameof(Kernels.Loop), 1, 1, [[7]]);
        uint offset = WarpPortableSchedulerLayout.HeapDescriptor;
        uint before = context.Arena[offset];
        if (corruptDescriptor) { context.Arena[offset] = uint.MaxValue; }
        try
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => new WarpCompiledSourceContext.SourceCompletionReceipt(
                context, null!, null!, null!, null!, new object()));
        }
        finally { context.Arena[offset] = before; }
    }

    [TestMethod]
    public async Task ActualOrdinaryCompletionKeepsOriginalFullHistoryAfterCommitAndRejectsReplacementIdentity()
    {
        WarpCompiledSourceContext context = Create(nameof(Kernels.Loop), 1, 1, [[7]]);
        RemoteModules modules = await RemoteModules.CreateAsync(context, new()).ConfigureAwait(false);
        await using var moduleOwner = modules.ConfigureAwait(false);
        WarpCoreCLRWorkerLease retained = modules.Source.Retain();
        await using var retainedOwner = retained.ConfigureAwait(false);
        WarpCompiledWorkerTicket ticket = context.Claim(0)!;
        Assert.ThrowsExactly<InvalidOperationException>(() => context.RequireCommittedSourceCheckpoint(ticket, modules.Source));
        var adapter = new WarpCompiledRemoteSourceAdapter(context, modules.Source, modules.CompareExchange, modules.Census);
        Assert.IsNull(await adapter.ExecuteAsync(await adapter.PrepareAsync(ticket).ConfigureAwait(false)).ConfigureAwait(false));
        WarpCompiledSourceContext.SourceCheckpoint checkpoint = context.RequireCommittedSourceCheckpoint(ticket, modules.Source);
        WarpCompiledSourceContext.SourceCompletionReceipt receipt = checkpoint.Completion!;
        Assert.IsNotNull(receipt);
        var request = receipt.CopyHistoricalRequest();
        var result = receipt.CopyHistoricalResult();
        AssertCompletedHistory(context, ticket, checkpoint, receipt, request, result);
        var copiedTicket = new WarpCompiledWorkerTicket(context, ticket.Worker, ticket.Physical, ticket.Generation, ticket.Dispatch);
        Assert.ThrowsExactly<InvalidOperationException>(() => receipt.ValidateHistorical(context, copiedTicket, modules.Source, checkpoint));
        Assert.ThrowsExactly<InvalidOperationException>(() => receipt.ValidateHistorical(context, ticket, retained, checkpoint));
        Assert.ThrowsExactly<InvalidOperationException>(() => receipt.ValidateHistorical(null!, ticket, modules.Source, checkpoint));
        WarpCompiledSourceContext other = Create(nameof(Kernels.Loop), 1, 1, [[7]]);
        Assert.ThrowsExactly<InvalidOperationException>(() => receipt.ValidateHistorical(other, ticket, modules.Source, checkpoint));
        uint[] bank = context.RemoteSourceInputs[0];
        context.RemoteSourceInputs[0] = (uint[])bank.Clone();
        Assert.ThrowsExactly<InvalidOperationException>(() => receipt.ValidateHistorical(context, ticket, modules.Source, checkpoint));
        context.RemoteSourceInputs[0] = bank;
        context.Commit(ticket);
        Assert.ThrowsExactly<InvalidOperationException>(() => checkpoint.Validate(context, ticket, modules.Source));
        receipt.ValidateHistorical(context, ticket, modules.Source, checkpoint);
        request.Inputs[0][0] ^= 1; result.State[^1] ^= 1; result.Arena[^1] ^= 1;
        CollectionAssert.AreEqual(bank, receipt.CopyHistoricalRequest().Inputs[0]);
        var original = receipt.CopyHistoricalResult();
        Assert.AreNotEqual(result.State[^1], original.State[^1]);
        Assert.AreNotEqual(result.Arena[^1], original.Arena[^1]);
        Assert.IsFalse(context.Plan.HasLocalSourceKernel);
    }

    private static void AssertCompletedHistory(WarpCompiledSourceContext context, WarpCompiledWorkerTicket ticket,
        WarpCompiledSourceContext.SourceCheckpoint checkpoint, WarpCompiledSourceContext.SourceCompletionReceipt receipt,
        WarpCoreCLRWorkerWords.Invocation request, (uint[] State, uint[] Arena) result)
    {
        Assert.IsEmpty(request.Scalars);
        Assert.AreEqual(checked((int)ticket.Worker), request.Worker);
        CollectionAssert.AreEqual(context.RemoteSourceInputs[0], request.Inputs[0]);
        CollectionAssert.AreEqual(context.MachineState(ticket.Worker).ToArray(), result.State);
        CollectionAssert.AreEqual(context.Arena, result.Arena);
        Assert.AreEqual(checkpoint.Ordinal, receipt.Ordinal);
        Assert.AreEqual(checkpoint.Sequence, receipt.Sequence);
        Assert.AreEqual(checkpoint.ProcessId, receipt.ProcessId);
        Assert.AreEqual(checkpoint.Module, receipt.Module);
    }
}
