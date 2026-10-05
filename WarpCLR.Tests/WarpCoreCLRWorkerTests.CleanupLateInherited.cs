using System.Collections.Concurrent;
using System.Diagnostics;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLRWorkerTests
{
    [TestMethod]
    public async Task LateInheritedCensusOnlyRetiresPositiveHeldIdentitiesAndKeepsTheFirstFailure()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Inconclusive("This gate specifically exercises Linux held pidfd terminal observations."); }
        using var drain = new ManualResetEventSlim();
        using Process sibling = StartIndependentPipeSentinel();
        var inherited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var signalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rows = new ConcurrentQueue<WarpCoreCLRCleanupEvidence>();
        int child = 0;
        var hooks = new WarpCoreCLRWorkerTestHooks(2)
        {
            Started = value => child = value, InheritedChild = value => inherited.SetResult(value), Killed = RecordKill,
            AfterContainmentSignal = () => { signalled.SetResult(); drain.Wait(); },
            CleanupMilestone = row => { rows.Enqueue(row); if (string.Equals(row.Milestone, "late-contained-terminal-failure", StringComparison.Ordinal)) { late.TrySetResult(); } },
        };
        Task<WarpCoreCLRWorkerKernel> compilation = WarpCoreCLRWorkerKernel.CompileAsync(new(RichKernel()),
            new() { CompilationTimeout = TimeSpan.FromSeconds(3) }, CancellationToken.None, hooks);
        try
        {
            int descendant = await inherited.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            using WarpCoreCLRWorkerPidFd descendantIdentity = WarpCoreCLRWorkerContainment.OpenPidFd(descendant);
            await signalled.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            WarpHostException failure;
            try { await compilation.ConfigureAwait(false); throw new AssertFailedException("Delayed inherited cleanup unexpectedly succeeded."); }
            catch (WarpHostException error) { failure = error; }
            Assert.AreEqual("WRPCORECLR3003", failure.Code, StringComparer.Ordinal);
            Assert.IsTrue(descendantIdentity.ObserveStopped()); AssertTerminated(child); Assert.IsFalse(sibling.HasExited);
            drain.Set(); await late.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            AssertLateInheritedOutcome(rows);
            await RecordCleanupAsync(nameof(LateInheritedCensusOnlyRetiresPositiveHeldIdentitiesAndKeepsTheFirstFailure), rows).ConfigureAwait(false);
        }
        finally
        {
            drain.Set(); sibling.Kill(); await sibling.WaitForExitAsync().ConfigureAwait(false);
            try { await compilation.ConfigureAwait(false); } catch (WarpHostException error) { TestContext.WriteLine(error.Message); }
        }
    }

    private static void AssertLateInheritedOutcome(ConcurrentQueue<WarpCoreCLRCleanupEvidence> rows)
    {
        Assert.HasCount(1, rows.Where(row => string.Equals(row.Milestone, "census-seal-end", StringComparison.Ordinal)).ToArray());
        Assert.HasCount(1, rows.Where(row => string.Equals(row.Milestone, "kill-start", StringComparison.Ordinal)).ToArray());
        Assert.HasCount(1, rows.Where(row => string.Equals(row.Milestone, "exit-observe-held-identities-only", StringComparison.Ordinal)).ToArray());
        Assert.IsFalse(rows.Any(row => string.Equals(row.Milestone, "succeeded", StringComparison.Ordinal)));
    }
}
