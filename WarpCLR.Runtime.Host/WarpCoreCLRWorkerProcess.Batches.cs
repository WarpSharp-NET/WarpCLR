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
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeToken);
        deadline.CancelAfter(options.QuantumTimeout);
        using WarpCoreCLRWordTransaction.BatchLease buffers = await WarpCoreCLRWordTransaction.AcquireBatchAsync(states, deadline.Token).ConfigureAwait(false);
        ValidateOrdinaryStorage(states, []);
        using WarpCoreCLRAsyncGate.Lease transaction = await transactions.AcquireAsync(deadline.Token).ConfigureAwait(false);
        long bytes = 52 + states.Length * 4L;
        foreach (uint[] state in states) { bytes += state.LongLength * sizeof(uint); }
        using WarpCoreCLRTransferAdmission.Lease storage = await WarpCoreCLRTransferAdmission.AcquireAsync(bytes * 8 + 4096, deadline.Token).ConfigureAwait(false);
        byte[] request = WarpCoreCLRWorkerBatchWords.Request(binding.Tag, binding.Identity, inputBase, depth, quantum, states);
        bool started = false;
        try
        {
            if (IsFaulted || binding.IsDisposed) { throw new InvalidOperationException("Batch storage lost its admitted live module or input binding."); }
            deadline.Token.ThrowIfCancellationRequested(); started = true;
            WarpCoreCLRWorkerProtocol.Frame response = await ControlAsync(WarpCoreCLRWorkerProtocol.ExecuteBatch, request, deadline.Token).ConfigureAwait(false);
            if (response.Kind != WarpCoreCLRWorkerProtocol.BatchExecuted) { throw new InvalidDataException("Native batch result is missing."); }
            uint[][] returned = WarpCoreCLRWorkerBatchWords.ReadResponse(response.Payload, request, states);
            for (int slot = 0; slot < states.Length; slot++) { ValidateResult(layout, states[slot], returned[slot], depth); }
            probes?.BeforeBatchPublication?.Invoke();
            lock (sync)
            {
                if (faulted) { throw new WarpHostException("WRPCORECLR3002", "A stopping CoreCLR worker cannot publish a returned batch."); }
                deadline.Token.ThrowIfCancellationRequested();
                for (int slot = 0; slot < states.Length; slot++) { returned[slot].CopyTo(states[slot], 0); }
            }
        }
        catch (Exception error)
        {
            if (started)
            {
                foreach (uint[] state in states) { WarpCoreCLRWordTransaction.Quarantine(state, []); }
                await DisposeAsync().ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            throw new WarpHostException("WRPCORECLR3002", "The bounded native batch failed; no returned logical state was committed.", error);
        }
    }
}
