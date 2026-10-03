namespace WarpCLR.Runtime.Host.Native;

internal interface IWarpNativeMachineExecution : IDisposable
{
    uint[] Resume(int quantum, CancellationToken cancellationToken = default);
}
