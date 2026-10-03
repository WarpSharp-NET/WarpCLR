namespace WarpCLR.Runtime.Host.Native;

internal sealed class WarpBoundNativeMachineExecution(
    WarpNativeMachineExecution execution,
    Action<Action> withExecutionContext,
    Action<Action> withCleanupContext,
    Action<WarpBoundNativeMachineExecution> released) : IWarpNativeMachineExecution
{
    private bool disposed;

    public uint[] Resume(int quantum, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        uint[]? result = null;
        withExecutionContext(() => result = execution.Resume(quantum, cancellationToken));
        return result!;
    }

    public void Dispose()
    {
        if (disposed) { return; }
        withCleanupContext(() =>
        {
            if (disposed) { return; }
            try { execution.Dispose(); }
            finally { disposed = true; released(this); }
        });
    }
}
