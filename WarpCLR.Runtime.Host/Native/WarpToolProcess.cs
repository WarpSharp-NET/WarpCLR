using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace WarpCLR.Runtime.Host.Native;

internal static class WarpToolProcess
{
    private const int MaximumDiagnosticCharacters = 65536;

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
        using Process process = Start(executable, arguments, directory);
        using var deadline = new CancellationTokenSource(timeout);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        Task<string> stdout = ReadBoundedAsync(process.StandardOutput, lifetime.Token);
        Task<string> stderr = ReadBoundedAsync(process.StandardError, lifetime.Token);
        try
        {
            await process.WaitForExitAsync(lifetime.Token).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).WaitAsync(lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException error)
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            cancellationToken.ThrowIfCancellationRequested();
            throw new WarpHostException("WRPNATIVE2004", $"Native tool '{executable}' exceeded its compilation deadline.", error);
        }

        var result = new WarpToolProcessResult(process.ExitCode,
            await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        if (result.ExitCode != 0)
        {
            throw new WarpHostException("WRPNATIVE2002",
                $"Native tool '{executable}' failed with exit code {result.ExitCode}: {result.StandardError}\n{result.StandardOutput}");
        }

        return result;
    }

    private static Process Start(string executable, IReadOnlyList<string> arguments, string? directory)
    {
        var process = new Process
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
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try { process.Start(); }
        catch (Exception error) when (error is Win32Exception or FileNotFoundException)
        {
            process.Dispose();
            throw new WarpHostException("WRPNATIVE2001", $"Required native tool '{executable}' could not be started.", error);
        }

        return process;
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        char[] buffer = new char[4096];
        var output = new StringBuilder();
        bool truncated = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
        {
            int available = MaximumDiagnosticCharacters - output.Length;
            output.Append(buffer, 0, Math.Min(count, available));
            truncated |= count > available;
        }

        if (truncated) { output.Append("\n[diagnostic output truncated]"); }
        return output.ToString();
    }
}
