using System.Collections.Concurrent;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLRWorkerTests
{
    [TestMethod]
    [DataRow("word", false)]
    [DataRow("word", true)]
    [DataRow("batch", false)]
    [DataRow("batch", true)]
    [DataRow("binding", false)]
    [DataRow("binding", true)]
    public async Task DisposalBeforeAuthenticatedPublicationCommitsNoResultBatchOrInputBinding(string operation, bool large)
    {
        using var publish = new ManualResetEventSlim();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rows = new ConcurrentQueue<WarpCoreCLRCleanupEvidence>();
        Action boundary = () => { ready.SetResult(); publish.Wait(); };
        var hooks = new WarpCoreCLRWorkerTestHooks
        {
            BeforeResultPublication = string.Equals(operation, "word", StringComparison.Ordinal) ? boundary : null,
            BeforeBatchPublication = string.Equals(operation, "batch", StringComparison.Ordinal) ? boundary : null,
            BeforeInputBindingPublication = string.Equals(operation, "binding", StringComparison.Ordinal) ? boundary : null,
            CleanupMilestone = rows.Enqueue,
        };
        var layout = new WarpLogicalMachineLayout(PublicationKernel(string.Equals(operation, "word", StringComparison.Ordinal)));
        WarpCoreCLRWorkerKernel child = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None, hooks).ConfigureAwait(false);
        WarpCoreCLRWorkerLease lease = child.TryAcquireLease()!;
        uint[] state = layout.CreateInitialState(1, 100), arena = [42], before = (uint[])state.Clone();
        Task source = StartPublicationProbeAsync(operation, lease, state, arena, large ? 4096 : layout.MaximumBlockCost);
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await child.DisposeAsync().ConfigureAwait(false);
            publish.Set();
            try { await source.ConfigureAwait(false); Assert.Fail("A stopped worker published an authenticated payload."); }
            catch (WarpHostException error) { Assert.AreEqual("WRPCORECLR3002", error.Code, StringComparer.Ordinal); }
            CollectionAssert.AreEqual(before, state); CollectionAssert.AreEqual(new uint[] { 42 }, arena);
            Assert.IsTrue(child.IsClosed); Assert.IsTrue(child.IsFaulted); Assert.IsNull(child.TryAcquireLease()); AssertTerminated(child.ProcessId);
            await RecordCleanupAsync(nameof(DisposalBeforeAuthenticatedPublicationCommitsNoResultBatchOrInputBinding) + "-" + operation + "-" + large, rows).ConfigureAwait(false);
        }
        finally { publish.Set(); await lease.DisposeAsync().ConfigureAwait(false); }
    }

    private static async Task StartPublicationProbeAsync(string operation, WarpCoreCLRWorkerLease lease, uint[] state, uint[] arena, int quantum)
    {
        if (string.Equals(operation, "word", StringComparison.Ordinal))
        { await lease.ExecuteManagedQuantumAsync([[17]], [], 0, state, 1, quantum, arena, CancellationToken.None).ConfigureAwait(false); return; }
        WarpCoreCLRInputBinding binding = await lease.BindInputsAsync([[17]], [], CancellationToken.None).ConfigureAwait(false);
        await using var owner = binding.ConfigureAwait(false);
        if (string.Equals(operation, "binding", StringComparison.Ordinal)) { Assert.Fail("Stopped input binding was published."); }
        await lease.ExecuteBatchAsync(binding, 0, [state], 1, quantum, CancellationToken.None).ConfigureAwait(false);
    }

    private static WarpControlFlowKernel PublicationKernel(bool arena)
    {
        var instructions = new List<WarpIrInstruction>
        { new(0, WarpIrOpCode.LoadInput), new(1, WarpIrOpCode.Constant, immediate: 7), new(2, WarpIrOpCode.Add, 0, 1) };
        if (arena) { instructions.Add(new(3, WarpIrOpCode.Constant)); instructions.Add(new(4, WarpIrOpCode.Constant, immediate: 123)); instructions.Add(new(5, WarpManagedMemoryOpCode.StoreWord, 3, 4)); }
        return new("cleanup-publication", 1, 0, [new WarpBasicBlock(0, [], instructions, new WarpReturnTerminator(2))]);
    }
}
