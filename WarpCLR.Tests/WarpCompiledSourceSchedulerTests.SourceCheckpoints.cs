using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpCompiledSourceSchedulerTests
{
    [TestMethod]
    public void CallerCannotConstructAnAuthenticatedSourceCheckpoint() =>
        Assert.ThrowsExactly<InvalidOperationException>(() => new WarpCompiledSourceContext.SourceCheckpoint(null!, null!, null!, null!, new object()));

    [TestMethod]
    public async Task CommittedSourceReceiptBindsTheExactContextTicketAndActualChildLease()
    {
        WarpCompiledSourceContext context = Create(nameof(Kernels.Loop), 1, 1, [[7]]);
        RemoteModules modules = await RemoteModules.CreateAsync(context, new()).ConfigureAwait(false);
        await using var ownedModules = modules.ConfigureAwait(false);
        WarpCoreCLRWorkerKernel second = await WarpCoreCLRWorkerKernel.CompileAsync(context.Plan.Layout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var secondOwner = second.ConfigureAwait(false);
        WarpCoreCLRWorkerLease other = second.TryAcquireLease()!;
        await using var otherOwner = other.ConfigureAwait(false);
        WarpCompiledWorkerTicket ticket = context.Claim(0)!;
        Assert.ThrowsExactly<InvalidOperationException>(() => context.RequireCommittedSourceCheckpoint(ticket, modules.Source));
        var adapter = new WarpCompiledRemoteSourceAdapter(context, modules.Source, modules.CompareExchange, modules.Census);
        Assert.IsNull(await adapter.ExecuteAsync(await adapter.PrepareAsync(ticket).ConfigureAwait(false)).ConfigureAwait(false));
        WarpCompiledSourceContext.SourceCheckpoint checkpoint = context.RequireCommittedSourceCheckpoint(ticket, modules.Source);
        Assert.AreEqual(context.Identity, checkpoint.ContextIdentity, StringComparer.Ordinal);
        Assert.AreEqual(context.Plan.Identity, checkpoint.PlanIdentity, StringComparer.Ordinal);
        Assert.AreEqual(context.Plan.CompilerIdentity.IdentityHash, checkpoint.CompilerIdentity, StringComparer.Ordinal);
        Assert.AreEqual(context.Dispatch, checkpoint.Dispatch);
        Assert.AreEqual(context.Epoch, checkpoint.Epoch);
        Assert.AreEqual(modules.Source.ProcessId, checkpoint.ProcessId);
        Assert.AreEqual(modules.Source.CompiledModule, checkpoint.Module);
        Assert.AreNotEqual(other.ProcessId, checkpoint.ProcessId);
        Assert.AreNotEqual(other.CompiledModule, checkpoint.Module);
        Assert.HasCount(64, checkpoint.RequestHash);
        Assert.HasCount(64, checkpoint.ResponseHash);
        Assert.IsGreaterThan(0UL, checkpoint.Ordinal);
        Assert.IsGreaterThanOrEqualTo(2UL, checkpoint.Sequence);
        checkpoint.Validate(context, ticket, modules.Source);
        var copied = new WarpCompiledWorkerTicket(context, ticket.Worker, ticket.Physical, ticket.Generation, ticket.Dispatch);
        Assert.ThrowsExactly<InvalidOperationException>(() => context.RequireCommittedSourceCheckpoint(copied, modules.Source));
        Assert.ThrowsExactly<InvalidOperationException>(() => checkpoint.Validate(context, copied, modules.Source));
        Assert.ThrowsExactly<InvalidOperationException>(() => checkpoint.Validate(context, ticket, other));
        Assert.ThrowsExactly<InvalidOperationException>(() => context.RequireCommittedSourceCheckpoint(ticket, other));
        Assert.ThrowsExactly<InvalidOperationException>(() => checkpoint.Validate(Create(nameof(Kernels.Loop), 1, 1, [[7]]), ticket, modules.Source));
        Assert.IsFalse(context.Plan.HasLocalSourceKernel);
        context.Commit(ticket);
        Assert.ThrowsExactly<InvalidOperationException>(() => checkpoint.Validate(context, ticket, modules.Source));
    }

    [TestMethod]
    public void EntryAllocationCannotLaunderAChangedCanonicalReadOnlyBank()
    {
        WarpCompiledSourceContext context = Create(nameof(Kernels.Identity), 1, 1, [new uint[1], new uint[1], new uint[1]]);
        context.QueueEntryAllocation(0, ObjectType(context));
        context.RemoteSourceInputs[0][0] ^= 1;
        uint[][] changed = context.RemoteSourceInputs.Select(static bank => (uint[])bank.Clone()).ToArray();
        InvalidOperationException failure = Assert.ThrowsExactly<InvalidOperationException>(() =>
        {
            for (int attempt = 0; attempt < 10000; attempt++)
            {
                WarpCompiledWorkerTicket ticket = context.Claim(0)!;
                Assert.IsNotNull(ticket);
                Assert.IsTrue(context.HasRuntimeHelper(ticket));
                context.Execute(ticket);
                context.Commit(ticket);
            }
            throw new AssertFailedException("The resumable entry allocation did not reach its guarded publication.");
        });
        StringAssert.Contains(failure.Message, "Entry allocation cannot replace changed canonical source argument words.", StringComparison.Ordinal);
        for (int bank = 0; bank < changed.Length; bank++)
        {
            CollectionAssert.AreEqual(changed[bank], context.RemoteSourceInputs[bank]);
        }
        Assert.ThrowsExactly<InvalidOperationException>(() => context.Result(0, context.Dispatch));
        Assert.IsNull(context.Claim(0));
    }

    [TestMethod]
    public async Task ChangedReadOnlyWordsBeforeSerializationCannotBecomeAnAdmittedSourceReceipt()
    {
        WarpCompiledSourceContext context = Create(nameof(Kernels.Loop), 1, 1, [[7]]);
        RemoteModules modules = await RemoteModules.CreateAsync(context, new()).ConfigureAwait(false);
        await using var ownedModules = modules.ConfigureAwait(false);
        WarpCompiledWorkerTicket ticket = context.Claim(0)!;
        var adapter = new WarpCompiledRemoteSourceAdapter(context, modules.Source, modules.CompareExchange, modules.Census);
        WarpCompiledRemotePreparedRun run = await adapter.PrepareAsync(ticket).ConfigureAwait(false);
        context.RemoteSourceInputs[0][0] ^= 1;
        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.ExecuteAsync(run)).ConfigureAwait(false);
        Assert.ThrowsExactly<InvalidOperationException>(() => context.RequireCommittedSourceCheckpoint(ticket, modules.Source));
        Assert.ThrowsExactly<InvalidOperationException>(() => context.Commit(ticket));
        Assert.ThrowsExactly<InvalidOperationException>(() => context.Result(ticket.Worker, context.Dispatch));
        Assert.IsFalse(context.Plan.HasLocalSourceKernel);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CompleteSourceBanksAndArgumentsRemainBoundToTheActualAuthenticatedCommand(bool changeArguments)
    {
        WarpCompiledSourceContext context = Create(nameof(Kernels.Loop), 1, 1, [[7]]);
        RemoteModules modules = await RemoteModules.CreateAsync(context, new()).ConfigureAwait(false);
        await using var ownedModules = modules.ConfigureAwait(false);
        WarpCompiledWorkerTicket ticket = context.Claim(0)!;
        var adapter = new WarpCompiledRemoteSourceAdapter(context, modules.Source, modules.CompareExchange, modules.Census);
        Assert.IsNull(await adapter.ExecuteAsync(await adapter.PrepareAsync(ticket).ConfigureAwait(false)).ConfigureAwait(false));
        WarpCompiledSourceContext.SourceCheckpoint checkpoint = context.RequireCommittedSourceCheckpoint(ticket, modules.Source);
        uint[] bank = changeArguments ? context.RemoteSourceInputs[0] : context.Arena;
        int index = changeArguments ? 0 : bank.Length - 1;
        bank[index] ^= 1;
        uint[] changed = (uint[])bank.Clone();
        Assert.ThrowsExactly<InvalidOperationException>(() => checkpoint.Validate(context, ticket, modules.Source));
        CollectionAssert.AreEqual(changed, bank);
        bank[index] ^= 1;
        checkpoint.Validate(context, ticket, modules.Source);
        context.Commit(ticket);
        Assert.ThrowsExactly<InvalidOperationException>(() => context.RequireCommittedSourceCheckpoint(ticket, modules.Source));
    }
}
