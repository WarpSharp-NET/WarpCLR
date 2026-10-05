using System.Collections.Concurrent;
using System.Diagnostics;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLRWorkerTests
{
    [TestMethod]
    public async Task ExitedLeaderPidfdContainsInheritedPipesAndPreservesParentAndSibling()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Inconclusive("This gate specifically exercises Linux stable pidfd groups."); }
        using Process sibling = StartIndependentPipeSentinel();
        int parentGroup = WarpCoreCLRWorkerContainment.GetProcessGroup(Environment.ProcessId), pid = 0;
        var inherited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var rows = new ConcurrentQueue<WarpCoreCLRCleanupEvidence>();
        WarpCoreCLRContainmentEvidence? receipt = null;
        using var leaderObservation = new HeldLeaderObservation();
        var hooks = new WarpCoreCLRWorkerTestHooks(3)
        {
            Started = value => { pid = value; leaderObservation.Capture(value); },
            InheritedChild = value => inherited.SetResult(value), CleanupMilestone = rows.Enqueue,
            Killed = evidence => { receipt = evidence; TestContext.WriteLine(evidence.ToString()); },
        };
        Task<WarpCoreCLRWorkerKernel> compilation = WarpCoreCLRWorkerKernel.CompileAsync(new(RichKernel()),
            new() { CompilationTimeout = TimeSpan.FromSeconds(10) }, CancellationToken.None, hooks);
        try
        {
            int descendant = await inherited.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await ObserveHeldLeaderExitAsync(leaderObservation.Identity).ConfigureAwait(false);
            Assert.AreEqual(pid, WarpCoreCLRWorkerContainment.GetProcessGroup(descendant));
            try { await compilation.ConfigureAwait(false); Assert.Fail("Exited leader unexpectedly returned a compiled module."); }
            catch (WarpHostException error) { Assert.AreEqual("WRPCORECLR3001", error.Code, StringComparer.Ordinal); }
            AssertExitedLeaderReceipt(receipt, pid, descendant, sibling, parentGroup);
            await RecordCleanupAsync(nameof(ExitedLeaderPidfdContainsInheritedPipesAndPreservesParentAndSibling), rows).ConfigureAwait(false);
        }
        finally
        {
            sibling.Kill(); await sibling.WaitForExitAsync().ConfigureAwait(false);
            try
            {
                WarpCoreCLRWorkerKernel unexpected = await compilation.ConfigureAwait(false);
                await unexpected.DisposeAsync().ConfigureAwait(false);
            }
            catch (WarpHostException error) { TestContext.WriteLine(error.Message); }
        }
    }

    private sealed class HeldLeaderObservation : IDisposable
    {
        private WarpCoreCLRWorkerPidFd? identity;
        internal WarpCoreCLRWorkerPidFd Identity => identity ?? throw new InvalidOperationException("The exact leader was not captured before bootstrap.");

        internal void Capture(int process)
        {
            if (identity is not null) { throw new InvalidOperationException("The exact leader observer was captured twice."); }
            identity = WarpCoreCLRWorkerContainment.OpenPidFd(process);
        }

        public void Dispose() => identity?.Dispose();
    }

    private static async Task ObserveHeldLeaderExitAsync(WarpCoreCLRWorkerPidFd identity)
    {
        long start = Stopwatch.GetTimestamp();
        while (!identity.ObserveStopped())
        {
            if (Stopwatch.GetElapsedTime(start) >= TimeSpan.FromSeconds(5)) { throw new AssertFailedException("The original held leader identity did not positively report exit."); }
            await Task.Delay(1).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task UnsupportedPidfdGroupSignalPreflightRejectsBeforeAnyChildStarts()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Inconclusive("This gate specifically exercises Linux stable pidfd groups."); }
        bool started = false;
        var hooks = new WarpCoreCLRWorkerTestHooks { GroupSignalProbeFlags = 8, Started = _ => started = true };
        int parent = WarpCoreCLRWorkerContainment.GetProcessGroup(Environment.ProcessId);
        PlatformNotSupportedException error = await Assert.ThrowsAsync<PlatformNotSupportedException>(() =>
            WarpCoreCLRWorkerKernel.CompileAsync(new(RichKernel()), new(), CancellationToken.None, hooks)).ConfigureAwait(false);
        StringAssert.Contains(error.Message, "errno=22", StringComparison.Ordinal);
        Assert.IsFalse(started); Assert.AreEqual(parent, WarpCoreCLRWorkerContainment.GetProcessGroup(Environment.ProcessId));
        TestContext.WriteLine(error.Message);
    }

    private static void AssertExitedLeaderReceipt(WarpCoreCLRContainmentEvidence? receipt, int pid, int descendant, Process sibling, int parentGroup)
    {
        Assert.IsNotNull(receipt); Assert.IsTrue(receipt.LeaderExited); Assert.IsTrue(receipt.StableHandle); Assert.IsTrue(receipt.GroupSignalled);
        Assert.AreEqual(pid, receipt.RegisteredPrivateGroup); Assert.AreNotEqual(parentGroup, receipt.RegisteredPrivateGroup);
        Assert.AreEqual(0, receipt.SignalResult); Assert.AreEqual(9, receipt.Signal);
        AssertTerminated(pid); AssertTerminated(descendant);
        Assert.IsFalse(sibling.HasExited); Assert.AreEqual(parentGroup, WarpCoreCLRWorkerContainment.GetProcessGroup(sibling.Id));
        Assert.AreEqual(parentGroup, WarpCoreCLRWorkerContainment.GetProcessGroup(Environment.ProcessId));
    }

    private static Process StartIndependentPipeSentinel()
    {
        var start = new ProcessStartInfo(WarpCoreCLRWorkerProcess.GetDotnetHost(new())) { UseShellExecute = false };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "WarpCLR.CoreCLR.Worker.dll")); start.ArgumentList.Add("--inherited-pipe-probe");
        return Process.Start(start) ?? throw new IOException("Independent sentinel failed to start.");
    }
}
