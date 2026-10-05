using System.Collections.Concurrent;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLRWorkerTests
{
    [TestMethod]
    public async Task LateContainmentNeverReopensTimedOutOutcomeSourceOrQuarantinedBuffers()
    {
        using var drain = new ManualResetEventSlim();
        var signalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rows = new ConcurrentQueue<WarpCoreCLRCleanupEvidence>();
        WarpCoreCLRWorkerTestHooks hooks = LateCleanupHooks(drain, signalled, late, rows);
        var layout = new WarpLogicalMachineLayout(InfiniteArenaKernel());
        LateCleanupBindings prepared = await LateCleanupBindings.CreateAsync(layout).ConfigureAwait(false);
        await using var preparedOwner = prepared.ConfigureAwait(false);
        WarpCoreCLRWorkerKernel child = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new() { QuantumTimeout = TimeSpan.FromSeconds(1) }, CancellationToken.None, hooks).ConfigureAwait(false);
        WarpCoreCLRWorkerLease lease = child.TryAcquireLease()!;
        uint[] state = prepared.Admission.State, arena = prepared.Admission.Arena, original = (uint[])state.Clone(), originalArena = (uint[])arena.Clone();
        Task source = lease.ExecuteOwnedManagedQuantumAsync([[0]], [], 0, state, 1, int.MaxValue, arena, prepared.Admission, CancellationToken.None);
        try
        {
            await signalled.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Task first = child.DisposeAsync().AsTask();
            Task[] callers = Enumerable.Range(0, 16).Select(_ => child.DisposeAsync().AsTask()).ToArray();
            try { await first.ConfigureAwait(false); Assert.Fail("Delayed cleanup unexpectedly succeeded."); }
            catch (WarpHostException error) { Assert.AreEqual("WRPCORECLR3003", error.Code, StringComparer.Ordinal); }
            AssertSharedFailedCleanupCallers(callers);
            WarpHostException failure;
            try { await source.ConfigureAwait(false); throw new AssertFailedException("Stopped source unexpectedly committed words."); }
            catch (WarpHostException error) { failure = error; }
            WarpCoreCLRStoppedCommands.Command command = AssertLateSourceFailure(failure, prepared.Admission.ExactTicket);
            await AssertStoppedBeforeDrainAsync(child, state, arena, original, originalArena).ConfigureAwait(false);
            drain.Set();
            await late.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await AssertLateCleanupTerminalAsync(child, lease, callers[0], state, arena, original, originalArena, rows).ConfigureAwait(false);
            Assert.Throws<InvalidOperationException>(() => WarpCoreCLRStoppedCommands.Mint(command, prepared.Admission.ExactTicket));
            await RecordCleanupAsync(nameof(LateContainmentNeverReopensTimedOutOutcomeSourceOrQuarantinedBuffers), rows).ConfigureAwait(false);
        }
        finally
        {
            drain.Set();
            try { await lease.DisposeAsync().ConfigureAwait(false); }
            catch (WarpHostException error) when (string.Equals(error.Code, "WRPCORECLR3003", StringComparison.Ordinal)) { }
        }
    }

    private WarpCoreCLRWorkerTestHooks LateCleanupHooks(ManualResetEventSlim drain, TaskCompletionSource signalled,
        TaskCompletionSource late, ConcurrentQueue<WarpCoreCLRCleanupEvidence> rows) => new()
    {
        AfterContainmentSignal = () => { signalled.SetResult(); drain.Wait(); },
        CleanupMilestone = row => { rows.Enqueue(row); if (string.Equals(row.Milestone, "late-contained-terminal-failure", StringComparison.Ordinal)) { late.TrySetResult(); } },
        Killed = RecordKill,
    };

    private static void AssertSharedFailedCleanupCallers(Task[] callers)
    {
        foreach (Task caller in callers) { Assert.AreSame(callers[0], caller); }
        Assert.IsTrue(callers.All(caller => caller.IsFaulted));
    }

    private static WarpCoreCLRStoppedCommands.Command AssertLateSourceFailure(WarpHostException failure, object exactTicket)
    {
        Assert.AreEqual("WRPCORECLR3002", failure.Code, StringComparer.Ordinal);
        WarpCoreCLRStoppedCommands.Command command = WarpCoreCLRStoppedCommands.FromFailure(failure)!;
        Assert.IsNotNull(command);
        Assert.Throws<InvalidOperationException>(() => WarpCoreCLRStoppedCommands.Mint(command, exactTicket));
        return command;
    }
    private static async Task AssertStoppedBeforeDrainAsync(WarpCoreCLRWorkerKernel child, uint[] state, uint[] arena, uint[] original, uint[] originalArena)
    {
        CollectionAssert.AreEqual(original, state); CollectionAssert.AreEqual(originalArena, arena);
        Assert.IsFalse(child.IsClosed); Assert.IsNull(child.TryAcquireLease()); AssertTerminated(child.ProcessId);
        await Assert.ThrowsAsync<WarpHostException>(() => WarpCoreCLRWordTransaction.AcquireAsync(state, arena, CancellationToken.None)).ConfigureAwait(false);
    }

    private static async Task AssertLateCleanupTerminalAsync(WarpCoreCLRWorkerKernel child, WarpCoreCLRWorkerLease lease, Task first,
        uint[] state, uint[] arena, uint[] original, uint[] originalArena, ConcurrentQueue<WarpCoreCLRCleanupEvidence> rows)
    {
        Assert.IsTrue(child.IsClosed); Assert.IsTrue(child.IsFaulted); Assert.IsNull(child.TryAcquireLease());
        Assert.IsFalse(child.HasSuccessfulContainment);
        Assert.AreSame(first, child.DisposeAsync().AsTask());
        await Assert.ThrowsAsync<WarpHostException>(() => child.DisposeAsync().AsTask()).ConfigureAwait(false);
        await Assert.ThrowsAsync<WarpHostException>(() => WarpCoreCLRWordTransaction.AcquireAsync(new uint[1], arena, CancellationToken.None)).ConfigureAwait(false);
        await Assert.ThrowsAsync<WarpHostException>(() => lease.ExecuteManagedQuantumAsync([[0]], [], 0, state, 1, int.MaxValue, arena, CancellationToken.None)).ConfigureAwait(false);
        CollectionAssert.AreEqual(original, state); CollectionAssert.AreEqual(originalArena, arena);
        Assert.HasCount(1, rows.Where(row => string.Equals(row.Milestone, "request", StringComparison.Ordinal)).ToArray());
        Assert.HasCount(1, rows.Where(row => string.Equals(row.Milestone, "kill-start", StringComparison.Ordinal)).ToArray());
    }

}
