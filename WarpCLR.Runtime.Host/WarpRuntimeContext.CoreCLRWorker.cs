using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

public sealed partial class WarpRuntimeContext
{
    private async Task<uint[]> ExecuteIsolatedAsync(WarpRuntimeEntry entry, WarpCoreCLRWorkerLease compiled,
        uint[][] inputs, uint[] scalars, int count, CancellationToken cancellationToken)
    {
        if (entry.Kernel.Execution is not null || entry.Layout.RequiresManagedMemory)
        { return await ExecuteLogicalIsolatedAsync(entry, compiled, inputs, scalars, count, cancellationToken).ConfigureAwait(false); }
        uint[] output = new uint[count];
        if (count == 0) { return entry.Reduction.HasValue ? [WarpReductionContract.GetDescriptor(entry.Reduction.Value).Identity] : output; }
        WarpCoreCLRInputBinding binding = await compiled.BindInputsAsync(inputs, scalars, cancellationToken).ConfigureAwait(false);
        await using var bindingOwner = binding.ConfigureAwait(false);
        int residentCount = Math.Min(count, Math.Min(options.MaximumResidentWorkers, WarpCLR.Backend.CoreCLR.WarpCoreCLRWorkerBatchWords.MaximumWorkers));
        uint[][] states = new uint[residentCount][];
        foreach (ref uint[] logicalState in states.AsSpan())
        { logicalState = entry.Layout.CreateInitialState(options.MaximumCallDepth, options.MaximumStepsPerWorker); }
        for (int batchBase = 0; batchBase < count;)
        {
            int batchCount = Math.Min(count - batchBase, residentCount);
            uint[][] active = states.AsSpan(0, batchCount).ToArray();
            foreach (uint[] logicalState in active) { entry.Layout.ResetState(logicalState, options.MaximumStepsPerWorker); }
            try
            { await compiled.ExecuteBatchAsync(binding, batchBase, active, options.MaximumCallDepth, options.ExecutionQuantum, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { if (compiled.IsFaulted) { MarkFaulted(); } throw; }
            catch (Exception error)
            { MarkFaulted(); throw new WarpRuntimeFaultException(entry.Identity, batchBase, WarpRuntimeFaultKind.RuntimeFailure, error); }
            for (int slot = 0; slot < batchCount; slot++)
            {
                if (active[slot][WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Faulted)
                { MarkFaulted(); throw CreateLogicalFault(entry, batchBase + slot, active[slot]); }
                output[batchBase + slot] = active[slot][WarpLogicalMachineLayout.ResultOffset];
            }
            batchBase += batchCount;
        }
        return entry.Reduction.HasValue ? [Reduce(entry.Reduction.Value, output,
            new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = options.MaximumParallelWorkers })] : output;
    }

    private async Task<uint[]> ExecuteLogicalIsolatedAsync(WarpRuntimeEntry entry, WarpCoreCLRWorkerLease compiled,
        uint[][] inputs, uint[] scalars, int count, CancellationToken cancellationToken)
    {
        uint[] output = new uint[count];
        int residentCount = Math.Min(count, options.MaximumResidentWorkers);
        uint[][] states = new uint[residentCount][];
        foreach (ref uint[] logicalState in states.AsSpan())
        { logicalState = entry.Layout.CreateInitialState(options.MaximumCallDepth, options.MaximumStepsPerWorker); }
        for (int batchBase = 0; batchBase < count;)
        {
            int batchCount = Math.Min(count - batchBase, residentCount);
            for (int slot = 0; slot < batchCount; slot++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                uint[] logicalState = states[slot];
                entry.Layout.ResetState(logicalState, options.MaximumStepsPerWorker);
                int worker = batchBase + slot;
                try
                {
                    while (logicalState[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable)
                    {
                        await compiled.ExecuteManagedQuantumAsync(inputs, scalars, worker, logicalState, options.MaximumCallDepth,
                            options.ExecutionQuantum, [], cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) { if (compiled.IsFaulted) { MarkFaulted(); } throw; }
                catch (Exception error)
                { MarkFaulted(); throw new WarpRuntimeFaultException(entry.Identity, worker, WarpRuntimeFaultKind.RuntimeFailure, error); }
                if (logicalState[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Faulted)
                { MarkFaulted(); throw CreateLogicalFault(entry, worker, logicalState); }
                output[worker] = logicalState[WarpLogicalMachineLayout.ResultOffset];
            }
            batchBase += batchCount;
        }
        return entry.Reduction.HasValue ? [Reduce(entry.Reduction.Value, output,
            new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = options.MaximumParallelWorkers })] : output;
    }
}
