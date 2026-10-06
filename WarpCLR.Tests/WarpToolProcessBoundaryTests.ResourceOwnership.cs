using System.Diagnostics;
using WarpCLR.Runtime.Host.Native;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpToolProcessBoundaryTests
{
    [TestMethod]
    public async Task ActualPendingReaderCancellationKeepsItsResourcesAfterTheFirstCleanupFailure()
    {
        RequireLinux();
        using var terminateEntered = new ManualResetEventSlim();
        using var releaseTermination = new ManualResetEventSlim();
        using var readerEntered = new ManualResetEventSlim();
        using var releaseReader = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        Process process = StartResourceOwnershipTool();
        int processId = process.Id;
        long readerTimestamp = 0;
        long terminationTimestamp = 0;
        using var ownership = new WarpToolProcessLifetime(process)
        {
            BeforeTermination = () => { Volatile.Write(ref terminationTimestamp, Stopwatch.GetTimestamp()); terminateEntered.Set(); releaseTermination.Wait(); },
            BeforeReaderCancellation = () => { Volatile.Write(ref readerTimestamp, Stopwatch.GetTimestamp()); readerEntered.Set(); releaseReader.Wait(); },
        };
        var execution = new WarpToolProcessExecution(process, ownership);
        Task<Exception?> operation = execution.RunAsync(cancellation.Token);
        try
        {
            await WaitForToolPrefixAsync(execution).ConfigureAwait(false);
            long request = Stopwatch.GetTimestamp();
            await cancellation.CancelAsync().ConfigureAwait(false);
            Assert.IsTrue(terminateEntered.Wait(TimeSpan.FromSeconds(5))); Assert.IsTrue(readerEntered.Wait(TimeSpan.FromSeconds(5)));
            WarpToolProcessOwnedExecution resources = ownership.RetainedExecution;
            Exception? failure = await operation.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
            long firstFailureObserved = AssertOriginalCancellation(failure, resources, cancellation.Token);
            var originalOutcome = CaptureToolOutcome(execution);
            ownership.Dispose();
            AssertPendingToolResources(process, processId, ownership, resources, execution);
            await FinishHeldResourceTasksAsync(process, ownership, resources, execution, operation, failure,
                releaseTermination, releaseReader).ConfigureAwait(false);
            AssertToolOutcomeUnchanged(execution, originalOutcome);
            TestContext.WriteLine($"Actual tool PID {processId}; cancel request {request}; first failure observed {firstFailureObserved}; elapsed {Stopwatch.GetElapsedTime(request, firstFailureObserved)}; configured cleanup deadline {WarpToolProcessExecution.CleanupDeadline}; termination entered {Volatile.Read(ref terminationTimestamp)}; reader cancellation entered {Volatile.Read(ref readerTimestamp)}; frequency {Stopwatch.Frequency}; retained task states {string.Join(',', resources.RetainedTasks.Select(static task => task.Status))}; observed faults {resources.ObservedFaults.Count}; first cleanup: {execution.Cleanup}");
        }
        finally
        {
            releaseTermination.Set(); releaseReader.Set();
            await cancellation.CancelAsync().ConfigureAwait(false);
            ownership.Dispose();
            await operation.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
            await ownership.Retirement.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
        }
    }

    private static long AssertOriginalCancellation(Exception? failure, WarpToolProcessOwnedExecution resources, CancellationToken token)
    {
        long observed = Stopwatch.GetTimestamp();
        Assert.IsInstanceOfType<OperationCanceledException>(failure);
        Assert.AreEqual(token, ((OperationCanceledException)failure).CancellationToken);
        Assert.AreEqual(TimeSpan.FromSeconds(2), WarpToolProcessExecution.CleanupDeadline);
        Assert.IsTrue(resources.CleanupToken.IsCancellationRequested,
            "The first failure must follow cancellation of the original cleanup deadline.");
        return observed;
    }

    private static (string Output, string Error, bool OutputEof, bool ErrorEof) CaptureToolOutcome(WarpToolProcessExecution execution) =>
        (execution.StandardOutput, execution.StandardError, execution.StandardOutputEndOfStream, execution.StandardErrorEndOfStream);

    private static void AssertToolOutcomeUnchanged(WarpToolProcessExecution execution,
        (string Output, string Error, bool OutputEof, bool ErrorEof) original)
    {
        Assert.AreEqual(original.Output, execution.StandardOutput, StringComparer.Ordinal);
        Assert.AreEqual(original.Error, execution.StandardError, StringComparer.Ordinal);
        Assert.AreEqual(original.OutputEof, execution.StandardOutputEndOfStream);
        Assert.AreEqual(original.ErrorEof, execution.StandardErrorEndOfStream);
    }

    private static async Task FinishHeldResourceTasksAsync(Process process, WarpToolProcessLifetime ownership,
        WarpToolProcessOwnedExecution resources, WarpToolProcessExecution execution, Task<Exception?> operation,
        Exception? failure, ManualResetEventSlim releaseTermination, ManualResetEventSlim releaseReader)
    {
        releaseTermination.Set();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
        AssertPendingCancellationAfterRootExit(ownership, resources, execution);
        releaseReader.Set();
        Task retirementCheck = AssertRetiredToolResourcesAsync(ownership, resources, execution, operation, failure);
        await Task.WhenAll([retirementCheck]).ConfigureAwait(false);
    }

    private static Process StartResourceOwnershipTool()
    {
        var start = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in TimeoutArguments) { start.ArgumentList.Add(argument); }
        return Process.Start(start) ?? throw new IOException("The actual resource ownership tool failed to start.");
    }

    private static void AssertPendingToolResources(Process process, int processId, WarpToolProcessLifetime ownership,
        WarpToolProcessOwnedExecution resources, WarpToolProcessExecution execution)
    {
        Assert.AreEqual(processId, process.Id); Assert.IsFalse(process.HasExited);
        Assert.IsFalse(ownership.Retirement.IsCompleted); Assert.IsFalse(resources.ResourcesRetired);
        Assert.IsFalse(resources.ReaderCancellationTask.IsCompleted);
        Assert.IsTrue(resources.RetainedTasks.Any(static task => !task.IsCompleted));
        Assert.IsTrue(resources.ReaderToken.IsCancellationRequested); Assert.IsTrue(resources.RootWaitToken.IsCancellationRequested);
        Assert.IsTrue(resources.CleanupToken.IsCancellationRequested);
        Assert.IsFalse(execution.RootExitObserved); Assert.IsTrue(execution.CleanupIncomplete);
        Assert.IsFalse(execution.StandardOutputEndOfStream); Assert.IsFalse(execution.StandardErrorEndOfStream);
        StringAssert.Contains(execution.StandardOutput, "compiler stdout before timeout", StringComparison.Ordinal);
        StringAssert.Contains(execution.StandardError, "compiler stderr before timeout", StringComparison.Ordinal);
    }

    private static void AssertPendingCancellationAfterRootExit(WarpToolProcessLifetime ownership,
        WarpToolProcessOwnedExecution resources, WarpToolProcessExecution execution)
    {
        Assert.IsFalse(ownership.Retirement.IsCompleted); Assert.IsFalse(resources.ResourcesRetired);
        Assert.IsFalse(resources.ReaderCancellationTask.IsCompleted);
        Assert.IsTrue(resources.ReaderToken.IsCancellationRequested); Assert.IsTrue(resources.RootWaitToken.IsCancellationRequested);
        Assert.IsTrue(resources.CleanupToken.IsCancellationRequested);
        Assert.IsFalse(execution.RootExitObserved, "Physical root exit cannot promote the original bounded observation.");
        Assert.IsTrue(execution.CleanupIncomplete);
    }

    private static async Task AssertRetiredToolResourcesAsync(WarpToolProcessLifetime ownership, WarpToolProcessOwnedExecution resources,
        WarpToolProcessExecution execution, Task<Exception?> operation, Exception? originalFailure)
    {
        await resources.Retirement.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
        await ownership.Retirement.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
        Assert.IsTrue(resources.ResourcesRetired); Assert.IsTrue(resources.ReaderCancellationTask.IsCompleted);
        Assert.IsTrue(resources.RetainedTasks.All(static task => task.IsCompleted));
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = resources.ReaderToken);
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = resources.RootWaitToken);
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = resources.CleanupToken);
        // Join this captured actual task without starting another operation or changing its result.
        Exception?[] completed = await Task.WhenAll([operation]).ConfigureAwait(false);
        Assert.AreSame(originalFailure, completed[0]);
        Assert.IsFalse(execution.RootExitObserved); Assert.IsTrue(execution.CleanupIncomplete);
        StringAssert.Contains(execution.StandardOutput, "compiler stdout before timeout", StringComparison.Ordinal);
        StringAssert.Contains(execution.StandardError, "compiler stderr before timeout", StringComparison.Ordinal);
    }
}
