using WarpCLR.IR;

namespace WarpCLR.Runtime.Host.Native;

internal interface IWarpNativeModule : IDisposable
{
    WarpNativeImage Image { get; }
    bool IsFaulted { get; }
    WarpNativeManagedArena CreateManagedArena(uint[] initial);
    IWarpNativeMachineExecution CreateMachineExecution(
        uint[] states,
        IReadOnlyList<uint[]> inputs,
        IReadOnlyList<uint> scalars,
        int itemCount,
        int inputBase,
        int maximumCallDepth,
        WarpNativeManagedArena? managedArena = null);
    uint[] DispatchUInt32(
        IReadOnlyList<uint[]> inputs,
        IReadOnlyList<uint> scalars,
        int itemCount,
        bool reduction,
        CancellationToken cancellationToken = default);
    uint[] ResumeUInt32(
        uint[] states,
        IReadOnlyList<uint[]> inputs,
        IReadOnlyList<uint> scalars,
        int itemCount,
        int inputBase,
        int maximumCallDepth,
        int quantum,
        CancellationToken cancellationToken = default);
    uint ReduceUInt32(uint[] values, WarpReductionOperation operation, CancellationToken cancellationToken = default);
}
