using System.Diagnostics;
using WarpCLR.Runtime.Host.Native;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpToolProcessBoundaryTests
{
    [TestMethod]
    public async Task DelayedActualTreeTerminationDoesNotDelayReaderStopOrPromoteALateSuccess()
    {
        RequireLinux();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var start = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in TimeoutArguments) { start.ArgumentList.Add(argument); }
        Process process = Process.Start(start)!;
        using var ownership = new WarpToolProcessLifetime(process)
        {
            BeforeTermination = () => { entered.Set(); release.Wait(); },
        };
        var execution = new WarpToolProcessExecution(process, ownership);
        Task<Exception?> operation = execution.RunAsync(cancellation.Token);
        try
        {
            await WaitForToolPrefixAsync(execution).ConfigureAwait(false);
            long request = Stopwatch.GetTimestamp();
            await cancellation.CancelAsync().ConfigureAwait(false);
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
            Exception? failure = await operation.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
            Assert.IsInstanceOfType<OperationCanceledException>(failure);
            Assert.IsTrue(execution.ReadersStopped);
            Assert.IsFalse(execution.RootExitObserved);
            Assert.IsFalse(execution.StandardOutputEndOfStream);
            Assert.IsFalse(execution.StandardErrorEndOfStream);
            Assert.IsTrue(execution.CleanupIncomplete);
            StringAssert.Contains(execution.StandardOutput, "compiler stdout before timeout", StringComparison.Ordinal);
            StringAssert.Contains(execution.StandardError, "compiler stderr before timeout", StringComparison.Ordinal);
            TestContext.WriteLine($"Exact tool PID {process.Id}; held actual termination; stopped readers at {Stopwatch.GetElapsedTime(request)}; cleanup: {execution.Cleanup}");
            release.Set();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
            Assert.IsFalse(execution.RootExitObserved, "A delayed termination cannot promote the original terminal cleanup observation.");
            Assert.IsTrue(execution.CleanupIncomplete);
        }
        finally
        {
            release.Set();
            await cancellation.CancelAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
            await operation.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
        }
    }

    private static async Task WaitForToolPrefixAsync(WarpToolProcessExecution execution)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!execution.StandardOutput.Contains("compiler stdout before timeout", StringComparison.Ordinal) ||
            !execution.StandardError.Contains("compiler stderr before timeout", StringComparison.Ordinal))
        { await Task.Delay(TimeSpan.FromMilliseconds(10), deadline.Token).ConfigureAwait(false); }
    }
}
