using WarpCLR.IR;

namespace WarpCLR.Runtime.Host.Native;

internal sealed class WarpNativeMachineExecution : IWarpNativeMachineExecution
{
    private readonly WarpNativeImage image;
    private readonly WarpMachineMemoryOperations operations;
    private readonly WarpNativeBankRetention bankUse;
    private uint[] stateSnapshot;
    private readonly uint[] scalars;
    private readonly IReadOnlyList<uint[]> inputs;
    private readonly List<ulong> inputPointers = [];
    private readonly int maximumItemCount;
    private readonly ulong[] previousBudgets;
    private readonly ulong[] previousOperations;
    private readonly uint[] previousBoundaries;
    private int itemCount;
    private int inputBase;
    private readonly int maximumCallDepth;
    private WarpNativeMachineLaunch launch;
    private readonly ulong statePointer;
    private WarpNativeManagedArena.Lease? arenaLease;
    private bool disposed;
    private bool cancelled;
    private bool faulted;

    public WarpNativeMachineExecution(WarpNativeImage image, uint[] states, IReadOnlyList<uint[]> inputs,
        IReadOnlyList<uint> scalars, int itemCount, int inputBase, int maximumCallDepth, WarpMachineMemoryOperations operations,
        WarpNativeManagedArena? managedArena = null)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(image);
        bankUse = WarpNativeBankRetention.Acquire(inputs, scalars, states);
        this.image = image;
        this.operations = operations;
        maximumItemCount = itemCount;
        this.itemCount = itemCount;
        this.inputBase = inputBase;
        this.maximumCallDepth = maximumCallDepth;
        try
        {
            int minimumQuantum = image.MachineLayout?.MaximumBlockCost ?? 1;
            launch = WarpNativeMachineLaunch.Admit(image, bankUse.State, bankUse.Inputs, bankUse.Scalars,
                itemCount, inputBase, maximumCallDepth, minimumQuantum, managedArena?.WordCount);
            stateSnapshot = (uint[])bankUse.State.Clone();
            previousBudgets = new ulong[itemCount];
            previousOperations = new ulong[itemCount];
            previousBoundaries = new uint[itemCount];
            CaptureBudgets(stateSnapshot, itemCount);
            this.scalars = bankUse.Scalars.ToArray();
            this.inputs = bankUse.Inputs;
            arenaLease = managedArena?.Acquire(image.Target, operations);
            if (itemCount == 0) { return; }
            using var state = new WarpPinnedUInt32(stateSnapshot);
            nuint bytes = checked((nuint)stateSnapshot.Length * sizeof(uint));
            statePointer = Allocate(bytes);
            operations.Upload(statePointer, state.Pointer, bytes);
            UploadInputs(this.inputs);
        }
        catch (Exception failure)
        {
            bankUse.RecordFailure(failure);
            Dispose();
            throw;
        }
    }

    public void ResetBatch(uint[] states, int itemCount, int inputBase)
    {
        EnsureUsable();
        using var nextUse = WarpNativeBankRetention.Acquire([], [], states);
        bool attached = false;
        try
        {
            if (itemCount > maximumItemCount)
            {
                throw new WarpHostException("WRPNATIVE1005", "The batch exceeds the logical execution's initially admitted resident capacity.");
            }
            WarpNativeMachineLaunch nextLaunch = WarpNativeMachineLaunch.Admit(image, nextUse.State, inputs, scalars,
                itemCount, inputBase, maximumCallDepth, image.MachineLayout!.MaximumBlockCost, arenaLease?.WordCount);
            uint[] nextSnapshot = (uint[])nextUse.State.Clone();
            // This upload reuses the original physical state buffer. Keep every
            // original batch bank until that exact buffer is positively freed.
            bankUse.AttachState(nextUse);
            attached = true;
            if (nextSnapshot.Length != 0)
            {
                using var state = new WarpPinnedUInt32(nextSnapshot);
                operations.Upload(statePointer, state.Pointer, checked((nuint)nextSnapshot.Length * sizeof(uint)));
            }
            stateSnapshot = nextSnapshot;
            CaptureBudgets(stateSnapshot, itemCount);
            this.itemCount = itemCount;
            this.inputBase = inputBase;
            launch = nextLaunch;
        }
        catch (Exception failure)
        {
            if (attached) { Quarantine(); }
            else
            {
                nextUse.RecordFailure(failure);
                nextUse.Retire(operations.Free, Quarantine);
            }
            throw;
        }
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
                checked((uint)itemCount), checked((uint)inputBase), checked((uint)maximumCallDepth), checked((uint)quantum),
                arenaLease?.Pointer ?? 0, arenaLease?.WordCount ?? 0, managedArena: arenaLease is not null);
            launched = true;
            operations.Launch(arguments.Pointer, launch.GridX, launch.WorkgroupSize);
            operations.Synchronize();
            using var states = new WarpPinnedUInt32(stateSnapshot);
            operations.Readback(states.Pointer, statePointer, checked((nuint)stateSnapshot.Length * sizeof(uint)));
            WarpNativeMachineLaunch.ValidateReturnedStates(image.MachineLayout!, stateSnapshot, itemCount, maximumCallDepth);
            observedLogicalFault = ValidateBudgetsAndCheckForFault();
            cancellationToken.ThrowIfCancellationRequested();
            uint[] result = (uint[])stateSnapshot.Clone();
            bankUse.RequirePublishable();
            return result;
        }
        catch (WarpHostException)
        {
            if (launched) { Quarantine(); }
            throw;
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            if (observedLogicalFault) { Quarantine(); }
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        bankUse.Retire(operations.Free, Quarantine);
        if (arenaLease is not null) { bankUse.AttemptAdditionalCleanup(arenaLease.Dispose); }
        arenaLease = null;
        bankUse.Dispose();
        bankUse.ThrowFirstFailure();
    }

    private void Quarantine()
    {
        faulted = true;
        arenaLease?.Quarantine();
        bankUse.RequestQuarantine(operations.Quarantine);
    }

    private void UploadInputs(IReadOnlyList<uint[]> inputs)
    {
        foreach (uint[] input in inputs)
        {
            nuint bytes = checked((nuint)Math.Max(input.Length, 1) * sizeof(uint));
            ulong pointer = Allocate(bytes);
            inputPointers.Add(pointer);
            // The exact admitted original bank stays retained through physical retirement;
            // pinning is needed only for this synchronous upload.
            using var pin = new WarpPinnedUInt32(input);
            if (input.Length != 0) { operations.Upload(pointer, pin.Pointer, bytes); }
        }
    }

    private ulong Allocate(nuint bytes) => bankUse.Allocate(bytes, operations.Allocate);

    private void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        arenaLease?.EnsureUsable();
        if (faulted) { throw new WarpHostException("WRPNATIVE1006", "The native logical execution has been quarantined."); }
        if (cancelled) { throw new OperationCanceledException("The logical execution was cancelled."); }
        bankUse.RequirePublishable();
    }

    private void CaptureBudgets(uint[] states, int count)
    {
        int stride = image.MachineLayout!.GetStateWords(maximumCallDepth);
        for (int worker = 0; worker < count; worker++)
        {
            previousBudgets[worker] = ReadBudget(states, worker * stride);
            previousOperations[worker] = ReadOperations(states, worker * stride);
            previousBoundaries[worker] = states[worker * stride + WarpLogicalMachineLayout.SourceBoundaryStateOffset];
        }
    }

    private bool ValidateBudgetsAndCheckForFault()
    {
        bool observedFault = false;
        int stride = image.MachineLayout!.GetStateWords(maximumCallDepth);
        for (int worker = 0; worker < itemCount; worker++)
        {
            ulong remaining = ReadBudget(stateSnapshot, worker * stride);
            uint status = stateSnapshot[worker * stride + WarpLogicalMachineLayout.StatusOffset];
            ulong used = ReadOperations(stateSnapshot, worker * stride);
            bool unchanged = remaining == previousBudgets[worker];
            bool helperProgress = image.MachineLayout!.HasLogicalAccounting && used > previousOperations[worker];
            uint boundary = stateSnapshot[worker * stride + WarpLogicalMachineLayout.SourceBoundaryStateOffset];
            bool boundaryProgress = image.MachineLayout!.HasLogicalAccounting && previousBoundaries[worker] == 0 &&
                boundary == WarpLogicalMachineLayout.BeforeSourceBoundary;
            if (remaining > previousBudgets[worker] || used < previousOperations[worker] ||
                (unchanged && status == WarpLogicalMachineLayout.Runnable && !helperProgress && !boundaryProgress))
            {
                throw new WarpHostException("WRPNATIVE1007", "A native worker increased its step budget or yielded runnable state without charged progress.");
            }

            previousBudgets[worker] = remaining;
            previousOperations[worker] = used;
            previousBoundaries[worker] = boundary;
            observedFault |= status == WarpLogicalMachineLayout.Faulted;
        }

        return observedFault;
    }

    private ulong ReadOperations(uint[] states, int offset) => image.MachineLayout!.HasLogicalAccounting ?
        states[offset + WarpLogicalMachineLayout.UsedOperationsLowOffset] |
        ((ulong)states[offset + WarpLogicalMachineLayout.UsedOperationsHighOffset] << 32) : 0;

    private static ulong ReadBudget(uint[] states, int offset) =>
        states[offset + WarpLogicalMachineLayout.RemainingStepsLowOffset] |
        ((ulong)states[offset + WarpLogicalMachineLayout.RemainingStepsHighOffset] << 32);
}
