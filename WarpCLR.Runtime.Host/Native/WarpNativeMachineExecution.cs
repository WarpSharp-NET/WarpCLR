using System.Runtime.InteropServices;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host.Native;

internal sealed class WarpNativeMachineExecution : IWarpNativeMachineExecution
{
    private readonly WarpNativeImage image;
    private readonly WarpMachineMemoryOperations operations;
    private readonly List<ulong> allocations = [];
    private readonly uint[] stateSnapshot;
    private readonly uint[] scalars;
    private readonly List<ulong> inputPointers = [];
    private readonly int itemCount;
    private readonly int inputBase;
    private readonly int maximumCallDepth;
    private readonly WarpNativeMachineLaunch launch;
    private ulong statePointer;
    private bool disposed;
    private bool cancelled;

    public WarpNativeMachineExecution(WarpNativeImage image, uint[] states, IReadOnlyList<uint[]> inputs,
        IReadOnlyList<uint> scalars, int itemCount, int inputBase, int maximumCallDepth, WarpMachineMemoryOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(image);
        int minimumQuantum = image.MachineLayout?.MaximumBlockCost ?? 1;
        launch = WarpNativeMachineLaunch.Admit(image, states, inputs, scalars, itemCount, inputBase, maximumCallDepth, minimumQuantum);
        this.image = image;
        this.operations = operations;
        this.itemCount = itemCount;
        this.inputBase = inputBase;
        this.maximumCallDepth = maximumCallDepth;
        stateSnapshot = (uint[])states.Clone();
        this.scalars = scalars.ToArray();
        if (itemCount == 0) { return; }
        try
        {
            using var state = new WarpPinnedUInt32(stateSnapshot);
            nuint bytes = checked((nuint)stateSnapshot.Length * sizeof(uint));
            statePointer = operations.Allocate(bytes);
            allocations.Add(statePointer);
            operations.Upload(statePointer, state.Pointer, bytes);
            UploadInputs(inputs);
        }
        catch { Dispose(); throw; }
    }

    public uint[] Resume(int quantum, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (cancelled) { throw new OperationCanceledException("The logical execution was cancelled."); }
        if (quantum < image.MachineLayout!.MaximumBlockCost)
        {
            throw new WarpHostException("WRPNATIVE1004", "The logical quantum cannot be smaller than a verified block's charged cost.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (itemCount == 0) { return []; }
        bool launched = false;
        try
        {
            using var arguments = new WarpNativeKernelArguments(statePointer, inputPointers, scalars,
                checked((uint)itemCount), checked((uint)inputBase), checked((uint)maximumCallDepth), checked((uint)quantum));
            launched = true;
            operations.Launch(arguments.Pointer, launch.GridX, launch.WorkgroupSize);
            operations.Synchronize();
            using var states = new WarpPinnedUInt32(stateSnapshot);
            operations.Readback(states.Pointer, statePointer, checked((nuint)stateSnapshot.Length * sizeof(uint)));
            cancellationToken.ThrowIfCancellationRequested();
            return (uint[])stateSnapshot.Clone();
        }
        catch (WarpHostException)
        {
            if (launched) { operations.Quarantine(); }
            throw;
        }
        catch (OperationCanceledException) { cancelled = true; throw; }
    }

    public void Dispose()
    {
        if (disposed) { return; }
        foreach (ref readonly ulong pointer in CollectionsMarshal.AsSpan(allocations)) { operations.Free(pointer); }
        allocations.Clear();
        disposed = true;
    }

    private void UploadInputs(IReadOnlyList<uint[]> inputs)
    {
        foreach (uint[] input in inputs)
        {
            nuint bytes = checked((nuint)Math.Max(input.Length, 1) * sizeof(uint));
            ulong pointer = operations.Allocate(bytes);
            allocations.Add(pointer);
            inputPointers.Add(pointer);
            // Runtime-owned immutable snapshots remain pinned only for the synchronous upload.
            using var pin = new WarpPinnedUInt32(input);
            if (input.Length != 0) { operations.Upload(pointer, pin.Pointer, bytes); }
        }
    }
}
