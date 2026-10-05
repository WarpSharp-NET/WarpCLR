namespace WarpCLR.Runtime.Host;

internal sealed class WarpCoreCLRInputBinding(WarpCoreCLRWorkerProcess process, uint tag, byte[] identity,
    WarpCoreCLRTransferAdmission.Lease retention) : IAsyncDisposable
{
    private int released;
    internal uint Tag => tag;
    internal byte[] Identity => identity;
    internal WarpCoreCLRWorkerProcess Process => process;
    internal bool IsDisposed => Volatile.Read(ref released) != 0;
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref released, 1) != 0) { return; }
        try { await process.UnbindInputsAsync(this).ConfigureAwait(false); }
        finally { retention.Dispose(); }
    }
}
