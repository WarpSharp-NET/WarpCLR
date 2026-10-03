namespace WarpCLR.Runtime.Host.Native;

internal sealed class WarpBoundNativeMachineExecution : IWarpNativeMachineExecution
{
    private readonly WarpNativeMachineExecution execution;
    private readonly Action<Action> withExecutionContext;
    private readonly Action<Action> withCleanupContext;
    private readonly Action<WarpBoundNativeMachineExecution> released;
    private bool disposed;

    public WarpBoundNativeMachineExecution(
        Func<WarpNativeMachineExecution> createExecution,
        Action<Action> withExecutionContext,
        Action<Action> withCleanupContext,
        Action<WarpBoundNativeMachineExecution> released)
    {
        ArgumentNullException.ThrowIfNull(createExecution);
        ArgumentNullException.ThrowIfNull(withExecutionContext);
        ArgumentNullException.ThrowIfNull(withCleanupContext);
        ArgumentNullException.ThrowIfNull(released);
        this.withExecutionContext = withExecutionContext;
        this.withCleanupContext = withCleanupContext;
        this.released = released;
        execution = createExecution();
    }

    public void ResetBatch(uint[] states, int itemCount, int inputBase)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        withExecutionContext(() => execution.ResetBatch(states, itemCount, inputBase));
    }

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
