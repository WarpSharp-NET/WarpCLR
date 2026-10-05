using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLRWorkerTests
{
    private static readonly JsonSerializerOptions CleanupJson = new() { WriteIndented = true };

    [TestMethod]
    public async Task ConcurrentDisposalSharesOnePreparedPrivateCleanupAndOneOutcome()
    {
        var rows = new ConcurrentQueue<WarpCoreCLRCleanupEvidence>();
        var hooks = new WarpCoreCLRWorkerTestHooks { CleanupMilestone = rows.Enqueue, Killed = RecordKill };
        WarpCoreCLRWorkerKernel child = await WarpCoreCLRWorkerKernel.CompileAsync(new(RichKernel()), new(), CancellationToken.None, hooks).ConfigureAwait(false);
        Task<Task>[] requests = Enumerable.Range(0, 16).Select(_ => Task.Factory.StartNew(() => child.DisposeAsync().AsTask(),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        Task[] callers = await Task.WhenAll(requests).ConfigureAwait(false);
        foreach (Task caller in callers) { Assert.AreSame(callers[0], caller); }
        await Task.WhenAll(callers).ConfigureAwait(false);
        Assert.AreSame(callers[0], child.DisposeAsync().AsTask());
        Assert.IsTrue(child.IsClosed); Assert.IsTrue(child.IsFaulted); Assert.IsNull(child.TryAcquireLease());
        AssertTerminated(child.ProcessId);
        Assert.HasCount(1, rows.Where(row => string.Equals(row.Milestone, "request", StringComparison.Ordinal)).ToArray());
        Assert.HasCount(1, rows.Where(row => string.Equals(row.Milestone, "kill-start", StringComparison.Ordinal)).ToArray());
        Assert.IsTrue(rows.Where(row => row.Milestone is "core-start" or "kill-start" or "cancel-start" or "pipes-start" or "exit-start" or "deadline-watch-start").All(row => !row.ThreadPool));
        await RecordCleanupAsync(nameof(ConcurrentDisposalSharesOnePreparedPrivateCleanupAndOneOutcome), rows).ConfigureAwait(false);
    }

    private async Task RecordCleanupAsync(string name, ConcurrentQueue<WarpCoreCLRCleanupEvidence> rows)
    {
        string evidence = JsonSerializer.Serialize(new { Stopwatch.Frequency, quotaSeconds = 2, rows = rows.ToArray() }, CleanupJson);
        string directory = Environment.GetEnvironmentVariable("WARP_CLEANUP_EVIDENCE") ?? TestContext.TestRunDirectory!;
        await File.WriteAllTextAsync(Path.Combine(directory, name + ".json"), evidence).ConfigureAwait(false);
        TestContext.WriteLine(evidence);
    }
}
