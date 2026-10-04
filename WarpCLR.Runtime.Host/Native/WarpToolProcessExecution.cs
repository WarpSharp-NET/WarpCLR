using System.Diagnostics;

namespace WarpCLR.Runtime.Host.Native;

internal sealed class WarpToolProcessExecution(Process process, WarpToolProcessLifetime ownership)
{
    private static readonly TimeSpan CleanupDeadline = TimeSpan.FromSeconds(2);
    private readonly WarpToolProcessCapture standardOutput = new();
    private readonly WarpToolProcessCapture standardError = new();

    public string StandardOutput => standardOutput.Snapshot();
    public string StandardError => standardError.Snapshot();
    public string Output => "stdout:" + Environment.NewLine + StandardOutput + Environment.NewLine + "stderr:" + Environment.NewLine + StandardError;
    public string Cleanup { get; private set; } = string.Empty;
    public bool CleanupIncomplete { get; private set; }
    public bool RootExitObserved { get; private set; }
    public bool ReadersStopped { get; private set; }
    public bool StandardOutputEndOfStream => standardOutput.EndOfStream;
    public bool StandardErrorEndOfStream => standardError.EndOfStream;

    public async Task<Exception?> RunAsync(CancellationToken deadline)
    {
        using var stopReaders = new CancellationTokenSource();
        using var stopRootWait = new CancellationTokenSource();
        Task stdout = standardOutput.DrainAsync(process.StandardOutput, stopReaders.Token);
        Task stderr = standardError.DrainAsync(process.StandardError, stopReaders.Token);
        Task exited = process.WaitForExitAsync(stopRootWait.Token);
        Exception failure;
        try
        {
            await Task.WhenAll(exited, stdout, stderr).WaitAsync(deadline).ConfigureAwait(false);
            RootExitObserved = true;
            ReadersStopped = true;
            return null;
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or InvalidOperationException)
        {
            failure = error;
        }

        using var cleanupDeadline = new CancellationTokenSource(CleanupDeadline);
        Task<string> termination = ownership.TerminateAsync();
        try
        {
            Cleanup = await termination.WaitAsync(cleanupDeadline.Token).ConfigureAwait(false);
            await stopReaders.CancelAsync().WaitAsync(cleanupDeadline.Token).ConfigureAwait(false);
            process.StandardOutput.Dispose();
            process.StandardError.Dispose();
            await Task.WhenAll(exited, stdout, stderr).WaitAsync(cleanupDeadline.Token).ConfigureAwait(false);
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
                await ShutdownAsync(stopReaders, stopRootWait, cleanupDeadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                CleanupIncomplete = true;
                Cleanup += " Cancellation cleanup exceeded its budget.";
            }
        }

        RootExitObserved = exited.IsCompletedSuccessfully;
        ReadersStopped = stdout.IsCompleted && stderr.IsCompleted;
        CleanupIncomplete |= !RootExitObserved || !ReadersStopped || !standardOutput.EndOfStream || !standardError.EndOfStream ||
            Cleanup.StartsWith("Tree termination failed:", StringComparison.Ordinal);
        Cleanup += " Root exit observed: " + RootExitObserved + "; readers stopped: " + ReadersStopped +
            "; stdout EOF: " + standardOutput.EndOfStream + "; stderr EOF: " + standardError.EndOfStream +
            ". Remaining descendant cleanup is unverified; the caller owns containment and recovery.";
        return failure;
    }

    private async Task ShutdownAsync(CancellationTokenSource stopReaders, CancellationTokenSource stopRootWait, CancellationToken deadline)
    {
        Task cancellation = Task.WhenAll(stopReaders.CancelAsync(), stopRootWait.CancelAsync());
        process.StandardOutput.Dispose();
        process.StandardError.Dispose();
        await cancellation.WaitAsync(deadline).ConfigureAwait(false);
    }
}
