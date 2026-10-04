using System.ComponentModel;
using System.Diagnostics;

namespace WarpCLR.Runtime.Host.Native;

internal static class WarpToolProcess
{
    public static async Task<WarpToolProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? directory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var elapsed = Stopwatch.StartNew();
        using WarpToolProcessLifetime ownership = Start(executable, arguments, directory);
        Process process = ownership.Process;
        using var deadline = new CancellationTokenSource(timeout);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var execution = new WarpToolProcessExecution(process, ownership);
        Exception? failure = await execution.RunAsync(lifetime.Token).ConfigureAwait(false);
        if (failure is not null)
        {
            string detail = $"Native tool '{executable}' (PID {process.Id}, elapsed {elapsed.Elapsed}) failed. " + execution.Output + Environment.NewLine + execution.Cleanup;
            Exception error = cancellationToken.IsCancellationRequested
                ? new OperationCanceledException(detail, failure, cancellationToken)
                : new WarpHostException(deadline.IsCancellationRequested ? "WRPNATIVE2004" : "WRPNATIVE2002",
                    deadline.IsCancellationRequested ? "The compilation deadline expired. " + detail : detail, failure);
            AddDiagnostics(error, process, elapsed, execution);
            throw error;
        }

        var result = new WarpToolProcessResult(process.ExitCode,
            execution.StandardOutput, execution.StandardError);
        if (result.ExitCode != 0)
        {
            var error = new WarpHostException("WRPNATIVE2002",
                $"Native tool '{executable}' (PID {process.Id}, elapsed {elapsed.Elapsed}) failed with exit code {result.ExitCode}. " + execution.Output);
            AddDiagnostics(error, process, elapsed, execution);
            error.Data["ExitCode"] = result.ExitCode;
            throw error;
        }

        return result;
    }

    private static WarpToolProcessLifetime Start(string executable, IReadOnlyList<string> arguments, string? directory)
    {
        Process? process = new()
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = directory ?? Environment.CurrentDirectory,
            },
        };
        try
        {
            foreach (string argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            try { process.Start(); }
            catch (Exception error) when (error is Win32Exception or FileNotFoundException)
            {
                throw new WarpHostException("WRPNATIVE2001", $"Required native tool '{executable}' could not be started.", error);
            }

            var ownership = new WarpToolProcessLifetime(process);
            process = null;
            return ownership;
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static void AddDiagnostics(Exception error, Process process, Stopwatch elapsed, WarpToolProcessExecution execution)
    {
        error.Data["ProcessId"] = process.Id;
        error.Data["Elapsed"] = elapsed.Elapsed;
        error.Data["StandardOutput"] = execution.StandardOutput;
        error.Data["StandardError"] = execution.StandardError;
        error.Data["RootExitObserved"] = execution.RootExitObserved;
        error.Data["ReadersStopped"] = execution.ReadersStopped;
        error.Data["StandardOutputEndOfStream"] = execution.StandardOutputEndOfStream;
        error.Data["StandardErrorEndOfStream"] = execution.StandardErrorEndOfStream;
        error.Data["CleanupIncomplete"] = execution.CleanupIncomplete;
        error.Data["Cleanup"] = execution.Cleanup;
    }
}
