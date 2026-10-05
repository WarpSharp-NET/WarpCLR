using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal sealed class WarpCoreCLRWorkerKernel : IAsyncDisposable
{
    private readonly Lock sync = new();
    private readonly WarpCoreCLRWorkerProcess worker;
    private int leases;
    private bool retired;

    private WarpCoreCLRWorkerKernel(WarpLogicalMachineLayout layout, WarpCoreCLRWorkerProcess worker)
    {
        Layout = layout; this.worker = worker;
    }

    internal WarpLogicalMachineLayout Layout { get; }
    internal int ProcessId => worker.ProcessId;
    internal Guid CompiledModule => worker.CompiledModule;
    internal bool IsCollectible => worker.IsCollectible;
    internal bool IsClosed => worker.IsClosed;
    internal bool HasSuccessfulContainment => worker.HasSuccessfulContainment;
    internal bool IsFaulted => worker.IsFaulted;

    internal static async Task<WarpCoreCLRWorkerKernel> CompileAsync(WarpLogicalMachineLayout layout,
        WarpCoreCLRWorkerOptions options, CancellationToken cancellationToken, WarpCoreCLRWorkerTestHooks? probes = null)
    {
        byte[] plan = WarpCoreCLRBinaryPlanCodec.Serialize(layout.Kernel);
        WarpCoreCLRWorkerProcess child = await WarpCoreCLRWorkerProcess.CompileAsync(plan, WarpIrHash.Compute(layout.Kernel), options,
            cancellationToken, probes).ConfigureAwait(false);
        return new WarpCoreCLRWorkerKernel(layout, child);
    }

    internal WarpCoreCLRWorkerLease? TryAcquireLease()
    {
        lock (sync)
        {
            if (retired || IsFaulted) { return null; }
            leases = checked(leases + 1);
            return new WarpCoreCLRWorkerLease(this);
        }
    }

    internal WarpCoreCLRWorkerProcess.WordCheckpoint RequireCommittedWordCheckpoint(uint[] state, uint[] arena) =>
        worker.RequireCommittedWordCheckpoint(state, arena);

    internal void ValidateCommittedWordCheckpoint(WarpCoreCLRWorkerProcess.WordCheckpoint checkpoint, uint[] state, uint[] arena) =>
        worker.ValidateCommittedWordCheckpoint(checkpoint, state, arena);

    internal Task ExecuteManagedQuantumAsync(uint[][] inputs, uint[] scalars, int workerIndex, uint[] state, int depth, int quantum,
        uint[] arena, CancellationToken cancellationToken) =>
        worker.ExecuteManagedQuantumAsync(Layout, inputs, scalars, workerIndex, state, depth, quantum, arena, cancellationToken);

    internal Task<WarpCoreCLRInputBinding> BindInputsAsync(uint[][] inputs, uint[] scalars, CancellationToken cancellationToken) =>
        worker.BindInputsAsync(inputs, scalars, cancellationToken);

    internal Task ExecuteBatchAsync(WarpCoreCLRInputBinding binding, int inputBase, uint[][] states, int depth, int quantum,
        CancellationToken cancellationToken) => worker.ExecuteBatchAsync(Layout, binding, inputBase, states, depth, quantum, cancellationToken);

    internal Task ExecuteOwnedManagedQuantumAsync(uint[][] inputs, uint[] scalars, int workerIndex, uint[] state, int depth, int quantum,
        uint[] arena, WarpCoreCLRCommandAdmission admission, CancellationToken cancellationToken) =>
        worker.ExecuteOwnedManagedQuantumAsync(Layout, inputs, scalars, workerIndex, state, depth, quantum, arena, admission, cancellationToken);

    internal Task ExecuteQuarantineQuantumAsync(uint[][] inputs, uint[] scalars, int workerIndex, uint[] state, int depth, int quantum,
        uint[] arena, WarpCoreCLRQuarantineRecovery recovery, CancellationToken cancellationToken) =>
        worker.ExecuteQuarantineQuantumAsync(Layout, inputs, scalars, workerIndex, state, depth, quantum, arena, recovery, cancellationToken);

    internal Task ExecuteOwnedControllerQuantumAsync(WarpCoreCLRControllerAdmission admission, int quantum, CancellationToken cancellationToken) =>
        worker.ExecuteControllerQuantumAsync(Layout, admission, quantum, cancellationToken);

    internal Task ExecuteControllerReleaseAsync(WarpCoreCLRControllerAdmission admission, uint[][] inputs, CancellationToken cancellationToken) =>
        worker.ExecuteControllerReleaseAsync(Layout, admission, inputs, cancellationToken);

    internal ValueTask ReleaseLeaseAsync()
    {
        lock (sync)
        {
            if (leases <= 0) { throw new InvalidOperationException("CoreCLR module lease was released twice."); }
            leases--;
            return retired && leases == 0 ? worker.DisposeAsync() : ValueTask.CompletedTask;
        }
    }

    internal ValueTask RetireAsync()
    {
        lock (sync) { retired = true; return leases == 0 ? worker.DisposeAsync() : ValueTask.CompletedTask; }
    }

    public ValueTask DisposeAsync()
    {
        lock (sync) { retired = true; return worker.DisposeAsync(); }
    }
}
