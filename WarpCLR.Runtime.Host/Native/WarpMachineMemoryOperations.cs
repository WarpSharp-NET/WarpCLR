namespace WarpCLR.Runtime.Host.Native;

internal sealed record WarpMachineMemoryOperations(
    Func<nuint, ulong> Allocate,
    Action<ulong, IntPtr, nuint> Upload,
    Action<IntPtr, ulong, nuint> Readback,
    Action<IntPtr, uint, uint> Launch,
    Action Synchronize,
    Action<ulong> Free,
    Action Quarantine,
    object? ContextIdentity = null);
