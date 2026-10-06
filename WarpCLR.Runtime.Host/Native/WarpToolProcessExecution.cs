using System.Diagnostics;

namespace WarpCLR.Runtime.Host.Native;

internal sealed class WarpToolProcessExecution(Process process, WarpToolProcessLifetime ownership)
{
    internal static readonly TimeSpan CleanupDeadline = TimeSpan.FromSeconds(2);
    private readonly WarpToolProcessCapture standardOutput = new();
    private readonly WarpToolProcessCapture standardError = new();
    private readonly Lock observationGate = new();
    private bool outcomeFrozen;
    private string frozenOutput = string.Empty;
    private string frozenError = string.Empty;
    private bool frozenOutputEndOfStream;
    private bool frozenErrorEndOfStream;

    public string StandardOutput { get { lock (observationGate) { return outcomeFrozen ? frozenOutput : standardOutput.Snapshot(); } } }
    public string StandardError { get { lock (observationGate) { return outcomeFrozen ? frozenError : standardError.Snapshot(); } } }
    public string Output => "stdout:" + Environment.NewLine + StandardOutput + Environment.NewLine + "stderr:" + Environment.NewLine + StandardError;
    public string Cleanup { get; private set; } = string.Empty;
    public bool CleanupIncomplete { get; private set; }
    public bool RootExitObserved { get; private set; }
    public bool ReadersStopped { get; private set; }
    public bool StandardOutputEndOfStream { get { lock (observationGate) { return outcomeFrozen ? frozenOutputEndOfStream : standardOutput.EndOfStream; } } }
    public bool StandardErrorEndOfStream { get { lock (observationGate) { return outcomeFrozen ? frozenErrorEndOfStream : standardError.EndOfStream; } } }

    public async Task<Exception?> RunAsync(CancellationToken deadline)
    {
        WarpToolProcessOwnedExecution resources = ownership.BeginExecution();
        try { return await resources.Own(RunOwnedAsync(resources, deadline)).ConfigureAwait(false); }
        finally { try { FreezeOutcome(); } finally { resources.Seal(); } }
    }

    private async Task<Exception?> RunOwnedAsync(WarpToolProcessOwnedExecution resources, CancellationToken deadline)
    {
        Task stdout = resources.Own(standardOutput.DrainAsync(process.StandardOutput, resources.ReaderToken));
        Task stderr = resources.Own(standardError.DrainAsync(process.StandardError, resources.ReaderToken));
        Task exited = resources.Own(process.WaitForExitAsync(resources.RootWaitToken));
        Task normal = resources.Own(Task.WhenAll(exited, stdout, stderr));
        Exception failure;
        try
        {
            await resources.Own(normal.WaitAsync(deadline)).ConfigureAwait(false);
            RootExitObserved = true;
            ReadersStopped = true;
            return null;
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or InvalidOperationException)
        {
            failure = error;
        }

        await resources.Own(CleanupAsync(resources, exited, stdout, stderr)).ConfigureAwait(false);
        CompleteObservations(exited, stdout, stderr);
        return failure;
    }

    private async Task CleanupAsync(WarpToolProcessOwnedExecution resources, Task exited, Task stdout, Task stderr)
    {
        CancellationToken cleanupDeadline = resources.StartCleanupDeadline(CleanupDeadline);
        Task<string> termination = resources.Own(ownership.TerminateAsync());
        Task readers = resources.Own(StopReadersAsync(resources, stdout, stderr, cleanupDeadline));
        try
        {
            Task<string> terminationWait = termination.WaitAsync(cleanupDeadline);
            _ = resources.Own(terminationWait);
            Cleanup = await terminationWait.ConfigureAwait(false);
            Task stopped = resources.Own(Task.WhenAll(exited, readers));
            await resources.Own(stopped.WaitAsync(cleanupDeadline)).ConfigureAwait(false);
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or InvalidOperationException)
        {
            Cleanup += " Cleanup exceeded its budget or failed: " + error.Message;
            CleanupIncomplete = true;
        }
        finally
        {
            try
            {
                // These exact drain/cancellation tasks were started by this execution.
                // Cached CancelAsync work has no caller-context dependency; retain it through retirement.
#pragma warning disable VSTHRD003 // Join owned cached cancellation tasks under the unchanged original deadline.
                await resources.Own(ShutdownAsync(resources, stdout, stderr, readers, cleanupDeadline)).ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                CleanupIncomplete = true;
                Cleanup += " Cancellation cleanup exceeded its budget.";
            }
        }

    }

    private void FreezeOutcome()
    {
        lock (observationGate)
        {
            if (outcomeFrozen) { return; }
            frozenOutput = standardOutput.Snapshot();
            frozenError = standardError.Snapshot();
            frozenOutputEndOfStream = standardOutput.EndOfStream;
            frozenErrorEndOfStream = standardError.EndOfStream;
            outcomeFrozen = true;
        }
    }

    private void CompleteObservations(Task exited, Task stdout, Task stderr)
    {
        FreezeOutcome();
        RootExitObserved = exited.IsCompletedSuccessfully;
        ReadersStopped = stdout.IsCompleted && stderr.IsCompleted;
        CleanupIncomplete |= !RootExitObserved || !ReadersStopped || !StandardOutputEndOfStream || !StandardErrorEndOfStream ||
            Cleanup.StartsWith("Tree termination failed:", StringComparison.Ordinal);
        Cleanup += " Root exit observed: " + RootExitObserved + "; readers stopped: " + ReadersStopped +
            "; stdout EOF: " + StandardOutputEndOfStream + "; stderr EOF: " + StandardErrorEndOfStream +
            ". Remaining descendant cleanup is unverified; the caller owns containment and recovery.";
    }

    private async Task StopReadersAsync(WarpToolProcessOwnedExecution resources, Task stdout, Task stderr, CancellationToken deadline)
    {
        await resources.Own(resources.CancelReadersAsync().WaitAsync(deadline)).ConfigureAwait(false);
        process.StandardOutput.Dispose();
        process.StandardError.Dispose();
        Task drains = resources.Own(Task.WhenAll(stdout, stderr));
        await resources.Own(drains.WaitAsync(deadline)).ConfigureAwait(false);
    }

    private async Task ShutdownAsync(WarpToolProcessOwnedExecution resources, Task stdout, Task stderr, Task readers, CancellationToken deadline)
    {
        Task cancellation = resources.Own(Task.WhenAll(resources.CancelReadersAsync(), resources.CancelRootWaitAsync()));
        process.StandardOutput.Dispose();
        process.StandardError.Dispose();
        Task stopped = resources.Own(Task.WhenAll(cancellation, stdout, stderr, readers));
        await resources.Own(stopped.WaitAsync(deadline)).ConfigureAwait(false);
    }
}
