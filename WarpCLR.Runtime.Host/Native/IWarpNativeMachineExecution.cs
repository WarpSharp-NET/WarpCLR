namespace WarpCLR.Runtime.Host.Native;

internal interface IWarpNativeMachineExecution : IDisposable
{
    void ResetBatch(uint[] states, int itemCount, int inputBase);
    uint[] Resume(int quantum, CancellationToken cancellationToken = default);
}
