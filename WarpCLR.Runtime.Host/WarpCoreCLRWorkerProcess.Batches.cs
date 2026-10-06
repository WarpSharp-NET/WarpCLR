using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCoreCLRWorkerProcess
{
    internal async Task ExecuteBatchAsync(WarpLogicalMachineLayout layout, WarpCoreCLRInputBinding binding, int inputBase,
        uint[][] states, int depth, int quantum, CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(binding.Process, this) || binding.IsDisposed || layout.Kernel.Execution is not null || layout.RequiresManagedMemory)
        { throw new InvalidOperationException("A native batch requires the exact live immutable binding and pure legacy map."); }
        using WarpCoreCLRInputBinding.BatchUse inputUse = binding.AcquireBatchUse(this);
        (WarpOrdinaryArrayAdmission ordinary, uint[][] capturedStates) = AdmitBoundBatch(inputUse, states);
        using WarpOrdinaryArrayAdmission ordinaryOwner = ordinary;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeToken);
        deadline.CancelAfter(options.QuantumTimeout);
        using WarpCoreCLRWordTransaction.BatchLease buffers = await WarpCoreCLRWordTransaction.AcquireBatchAsync(capturedStates, deadline.Token).ConfigureAwait(false);
        ValidateOrdinaryStorage(capturedStates, []);
        using WarpCoreCLRAsyncGate.Lease transaction = await transactions.AcquireAsync(deadline.Token).ConfigureAwait(false);
        long bytes = 52 + capturedStates.Length * 4L;
        foreach (uint[] state in capturedStates) { bytes += state.LongLength * sizeof(uint); }
        using WarpCoreCLRTransferAdmission.Lease storage = await WarpCoreCLRTransferAdmission.AcquireAsync(bytes * 8 + 4096, deadline.Token).ConfigureAwait(false);
        byte[] request = WarpCoreCLRWorkerBatchWords.Request(binding.Tag, binding.Identity, inputBase, depth, quantum, capturedStates);
        bool started = false;
        try
        {
            // An already acquired binding use remains valid while disposal waits for it to drain.
            if (IsFaulted) { throw new InvalidOperationException("Batch storage lost its admitted live module."); }
            deadline.Token.ThrowIfCancellationRequested(); started = true;
            WarpCoreCLRWorkerProtocol.Frame response = await ControlAsync(WarpCoreCLRWorkerProtocol.ExecuteBatch, request, deadline.Token).ConfigureAwait(false);
            if (response.Kind != WarpCoreCLRWorkerProtocol.BatchExecuted) { throw new InvalidDataException("Native batch result is missing."); }
            uint[][] returned = WarpCoreCLRWorkerBatchWords.ReadResponse(response.Payload, request, capturedStates);
            for (int slot = 0; slot < capturedStates.Length; slot++) { ValidateResult(layout, capturedStates[slot], returned[slot], depth); }
            probes?.BeforeBatchPublication?.Invoke();
            lock (sync)
            {
                if (faulted) { throw new WarpHostException("WRPCORECLR3002", "A stopping CoreCLR worker cannot publish a returned batch."); }
                deadline.Token.ThrowIfCancellationRequested();
                for (int slot = 0; slot < capturedStates.Length; slot++) { returned[slot].CopyTo(capturedStates[slot], 0); }
            }
        }
        catch (Exception error)
        {
            if (started)
            {
                foreach (uint[] state in capturedStates) { WarpCoreCLRWordTransaction.Quarantine(state, []); }
                await DisposeAsync().ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            throw new WarpHostException("WRPCORECLR3002", "The bounded native batch failed; no returned logical state was committed.", error);
        }
    }
}
