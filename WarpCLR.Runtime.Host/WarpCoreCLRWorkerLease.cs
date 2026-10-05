using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal sealed class WarpCoreCLRWorkerLease(WarpCoreCLRWorkerKernel kernel) : IAsyncDisposable
{
    private int released;
    internal WarpLogicalMachineLayout Layout => kernel.Layout;
    internal int ProcessId => kernel.ProcessId;
    internal Guid CompiledModule => kernel.CompiledModule;
    internal bool IsCollectible => kernel.IsCollectible;
    internal bool IsFaulted => kernel.IsFaulted;
    internal bool IsReleased => Volatile.Read(ref released) != 0;

    internal WarpCoreCLRWorkerLease Retain()
    {
        ObjectDisposedException.ThrowIf(IsReleased, this);
        return kernel.TryAcquireLease() ?? throw new InvalidOperationException("The exact prepared module can no longer be retained.");
    }

    internal Task ExecuteOwnedControllerQuantumAsync(WarpCoreCLRControllerAdmission admission, int quantum,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsReleased, this);
        return kernel.ExecuteOwnedControllerQuantumAsync(admission, quantum, cancellationToken);
    }

    internal Task ExecuteControllerReleaseAsync(WarpCoreCLRControllerAdmission admission, uint[][] inputs, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(IsReleased, this);
        return kernel.ExecuteControllerReleaseAsync(admission, inputs, cancellationToken);
    }

    internal Task<WarpCoreCLRInputBinding> BindInputsAsync(uint[][] inputs, uint[] scalars, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref released) != 0, this);
        return kernel.BindInputsAsync(inputs, scalars, cancellationToken);
    }

    internal Task ExecuteBatchAsync(WarpCoreCLRInputBinding binding, int inputBase, uint[][] states, int depth, int quantum,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref released) != 0, this);
        return kernel.ExecuteBatchAsync(binding, inputBase, states, depth, quantum, cancellationToken);
    }

    internal Task ExecuteManagedQuantumAsync(uint[][] inputs, uint[] scalars, int logicalWorkerIndex, uint[] state,
        int maximumLogicalCallDepth, int quantum, uint[] persistentArena, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref released) != 0, this);
        return kernel.ExecuteManagedQuantumAsync(inputs, scalars, logicalWorkerIndex, state, maximumLogicalCallDepth, quantum, persistentArena, cancellationToken);
    }

    internal Task ExecuteOwnedManagedQuantumAsync(uint[][] inputs, uint[] scalars, int logicalWorkerIndex, uint[] state,
        int maximumLogicalCallDepth, int quantum, uint[] persistentArena, WarpCoreCLRCommandAdmission admission,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref released) != 0, this);
        return kernel.ExecuteOwnedManagedQuantumAsync(inputs, scalars, logicalWorkerIndex, state, maximumLogicalCallDepth, quantum,
            persistentArena, admission, cancellationToken);
    }

    internal Task ExecuteQuarantineQuantumAsync(uint[][] inputs, uint[] scalars, int logicalWorkerIndex, uint[] state,
        int maximumLogicalCallDepth, int quantum, uint[] persistentArena, WarpCoreCLRQuarantineRecovery recovery,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref released) != 0, this);
        return kernel.ExecuteQuarantineQuantumAsync(inputs, scalars, logicalWorkerIndex, state, maximumLogicalCallDepth, quantum,
            persistentArena, recovery, cancellationToken);
    }

    public ValueTask DisposeAsync() => Interlocked.Exchange(ref released, 1) == 0 ? kernel.ReleaseLeaseAsync() : ValueTask.CompletedTask;
}
