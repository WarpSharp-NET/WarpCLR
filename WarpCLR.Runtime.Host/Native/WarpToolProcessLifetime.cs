using System.ComponentModel;
using System.Diagnostics;

namespace WarpCLR.Runtime.Host.Native;

internal sealed class WarpToolProcessLifetime : IDisposable
{
    private readonly Process process;
    private readonly Lock gate = new();
    private bool terminationPending;
    private bool disposeRequested;

    public WarpToolProcessLifetime(Process process)
    {
        this.process = process;
    }

    public Process Process => process;

    public Task<string> TerminateAsync()
    {
        lock (gate) { terminationPending = true; }
        return Task.Run(() =>
        {
            try
            {
                if (process.HasExited)
                {
                    return "Root already exited; remaining descendants cannot be identified from this root.";
                }

                process.Kill(entireProcessTree: true);
                return "Tree termination requested; descendant exit is not confirmed.";
            }
            catch (Exception error) when (error is Win32Exception or InvalidOperationException or NotSupportedException or AggregateException)
            {
                return "Tree termination failed: " + error;
            }
            finally
            {
                lock (gate)
                {
                    terminationPending = false;
                    if (disposeRequested) { process.Dispose(); }
                }
            }
        });
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposeRequested = true;
            // A non-preemptible termination call keeps ownership until it returns.
            if (!terminationPending) { process.Dispose(); }
        }
    }
}
