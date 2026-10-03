using System.Runtime.InteropServices;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host.Native;

internal sealed class WarpNativeMachineExecution : IWarpNativeMachineExecution
{
    private readonly WarpNativeImage image;
    private readonly WarpMachineMemoryOperations operations;
    private readonly List<ulong> allocations = [];
    private uint[] stateSnapshot;
    private readonly uint[] scalars;
    private readonly uint[][] inputs;
    private readonly List<ulong> inputPointers = [];
    private readonly int maximumItemCount;
    private readonly ulong[] previousBudgets;
    private int itemCount;
    private int inputBase;
    private readonly int maximumCallDepth;
    private WarpNativeMachineLaunch launch;
    private readonly ulong statePointer;
    private bool disposed;
    private bool cancelled;
    private bool faulted;

    public WarpNativeMachineExecution(WarpNativeImage image, uint[] states, IReadOnlyList<uint[]> inputs,
        IReadOnlyList<uint> scalars, int itemCount, int inputBase, int maximumCallDepth, WarpMachineMemoryOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(image);
        int minimumQuantum = image.MachineLayout?.MaximumBlockCost ?? 1;
        launch = WarpNativeMachineLaunch.Admit(image, states, inputs, scalars, itemCount, inputBase, maximumCallDepth, minimumQuantum);
        this.image = image;
        this.operations = operations;
        maximumItemCount = itemCount;
        this.itemCount = itemCount;
        this.inputBase = inputBase;
        this.maximumCallDepth = maximumCallDepth;
        stateSnapshot = (uint[])states.Clone();
        previousBudgets = new ulong[itemCount];
        CaptureBudgets(stateSnapshot, itemCount);
        this.scalars = scalars.ToArray();
        this.inputs = inputs.ToArray();
        if (itemCount == 0) { return; }
        try
        {
            using var state = new WarpPinnedUInt32(stateSnapshot);
            nuint bytes = checked((nuint)stateSnapshot.Length * sizeof(uint));
            statePointer = operations.Allocate(bytes);
            allocations.Add(statePointer);
            operations.Upload(statePointer, state.Pointer, bytes);
            UploadInputs(this.inputs);
        }
        catch { Dispose(); throw; }
    }

    public void ResetBatch(uint[] states, int itemCount, int inputBase)
    {
        EnsureUsable();
        if (itemCount > maximumItemCount)
        {
            throw new WarpHostException("WRPNATIVE1005", "The batch exceeds the logical execution's initially admitted resident capacity.");
        }

        WarpNativeMachineLaunch nextLaunch = WarpNativeMachineLaunch.Admit(image, states, inputs, scalars,
            itemCount, inputBase, maximumCallDepth, image.MachineLayout!.MaximumBlockCost);
        uint[] nextSnapshot = (uint[])states.Clone();
        try
        {
            if (nextSnapshot.Length != 0)
            {
                using var state = new WarpPinnedUInt32(nextSnapshot);
                operations.Upload(statePointer, state.Pointer, checked((nuint)nextSnapshot.Length * sizeof(uint)));
            }
        }
        catch (WarpHostException) { faulted = true; operations.Quarantine(); throw; }

        stateSnapshot = nextSnapshot;
        CaptureBudgets(stateSnapshot, itemCount);
        this.itemCount = itemCount;
        this.inputBase = inputBase;
        launch = nextLaunch;
    }

    public uint[] Resume(int quantum, CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        if (quantum < image.MachineLayout!.MaximumBlockCost)
        {
            throw new WarpHostException("WRPNATIVE1004", "The logical quantum cannot be smaller than a verified block's charged cost.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (itemCount == 0) { return []; }
        bool launched = false;
        bool observedLogicalFault = false;
        try
        {
            using var arguments = new WarpNativeKernelArguments(statePointer, inputPointers, scalars,
                checked((uint)itemCount), checked((uint)inputBase), checked((uint)maximumCallDepth), checked((uint)quantum));
            launched = true;
            operations.Launch(arguments.Pointer, launch.GridX, launch.WorkgroupSize);
            operations.Synchronize();
            using var states = new WarpPinnedUInt32(stateSnapshot);
            operations.Readback(states.Pointer, statePointer, checked((nuint)stateSnapshot.Length * sizeof(uint)));
            WarpNativeMachineLaunch.ValidateReturnedStates(image.MachineLayout!, stateSnapshot, itemCount, maximumCallDepth);
            observedLogicalFault = ValidateBudgetsAndCheckForFault();
            cancellationToken.ThrowIfCancellationRequested();
            return (uint[])stateSnapshot.Clone();
        }
        catch (WarpHostException)
        {
            if (launched) { faulted = true; operations.Quarantine(); }
            throw;
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            if (observedLogicalFault) { faulted = true; operations.Quarantine(); }
            throw;
        }
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

    private void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (faulted) { throw new WarpHostException("WRPNATIVE1006", "The native logical execution has been quarantined."); }
        if (cancelled) { throw new OperationCanceledException("The logical execution was cancelled."); }
    }

    private void CaptureBudgets(uint[] states, int count)
    {
        int stride = image.MachineLayout!.GetStateWords(maximumCallDepth);
        for (int worker = 0; worker < count; worker++) { previousBudgets[worker] = ReadBudget(states, worker * stride); }
    }

    private bool ValidateBudgetsAndCheckForFault()
    {
        bool observedFault = false;
        int stride = image.MachineLayout!.GetStateWords(maximumCallDepth);
        for (int worker = 0; worker < itemCount; worker++)
        {
            ulong remaining = ReadBudget(stateSnapshot, worker * stride);
            uint status = stateSnapshot[worker * stride + WarpLogicalMachineLayout.StatusOffset];
            if (remaining > previousBudgets[worker] || (remaining == previousBudgets[worker] && status == WarpLogicalMachineLayout.Runnable))
            {
                throw new WarpHostException("WRPNATIVE1007", "A native worker increased its step budget or yielded runnable state without charged progress.");
            }

            previousBudgets[worker] = remaining;
            observedFault |= status == WarpLogicalMachineLayout.Faulted;
        }

        return observedFault;
    }

    private static ulong ReadBudget(uint[] states, int offset) =>
        states[offset + WarpLogicalMachineLayout.RemainingStepsLowOffset] |
        ((ulong)states[offset + WarpLogicalMachineLayout.RemainingStepsHighOffset] << 32);
}
