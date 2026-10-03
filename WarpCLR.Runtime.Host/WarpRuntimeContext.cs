using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

public sealed class WarpRuntimeContext : IAsyncDisposable
{
    private readonly Lock sync = new();
    private readonly WarpRuntimeModule module;
    private readonly WarpRuntimeOptions options;
    private readonly WarpJitCache jitCache;
    private readonly bool ownsJitCache;
    private readonly WarpNativeExecutionProvider? nativeProvider;
    private readonly SemaphoreSlim dispatchSlots;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim drained = new(0, 1);
    private WarpRuntimeContextState state;
    private int activeDispatches;
    private long admittedBytes;
    private Task? disposal;

    public WarpRuntimeContext(WarpRuntimeModule module, WarpBackendKind backend, WarpRuntimeOptions? options = null, WarpJitCache? jitCache = null)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (!WarpBackendCatalog.Required.Contains(backend))
        {
            throw new WarpHostException("WRPRUNTIME1003", "The selected backend is not registered. No backend fallback is permitted.");
        }

        this.module = module;
        this.options = options ?? new WarpRuntimeOptions();
        WarpRuntimeOptions.Validate(this.options);
        this.jitCache = jitCache ?? new WarpJitCache();
        ownsJitCache = jitCache is null;
        nativeProvider = backend == WarpBackendKind.CoreCLR ? null : new WarpNativeExecutionProvider(module, this.jitCache, backend, this.options.Native);
        dispatchSlots = new SemaphoreSlim(this.options.MaximumConcurrentDispatches);
        Backend = backend;
    }

    public WarpBackendKind Backend { get; }

    public WarpRuntimeContextState State
    {
        get
        {
            lock (sync)
            {
                return state;
            }
        }
    }

    public WarpJitCacheStatistics JitStatistics => jitCache.Statistics;

    public Task<uint[]> DispatchIntegerMapAsync(string entryIdentity, IReadOnlyList<uint[]> inputs, IReadOnlyList<uint>? scalarArguments = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        WarpRuntimeEntry entry = GetEntry(entryIdentity);
        if (entry.Reduction.HasValue)
        {
            throw new WarpHostException("WRPRUNTIME1004", "The requested entry is a reduction, not a map.");
        }

        return DispatchAsync(entry, inputs, scalarArguments, cancellationToken);
    }

    public async Task<uint> DispatchUInt32ReductionAsync(string entryIdentity, IReadOnlyList<uint[]> inputs, IReadOnlyList<uint>? scalarArguments = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        WarpRuntimeEntry entry = GetEntry(entryIdentity);
        if (!entry.Reduction.HasValue)
        {
            throw new WarpHostException("WRPRUNTIME1004", "The requested entry is a map, not a reduction.");
        }

        uint[] result = await DispatchAsync(entry, inputs, scalarArguments, cancellationToken).ConfigureAwait(false);
        return result[0];
    }

    public ValueTask DisposeAsync()
    {
        lock (sync)
        {
            disposal ??= DisposeCoreAsync();
            return new ValueTask(disposal);
        }
    }

    private async Task DisposeCoreAsync()
    {
        lock (sync)
        {
            state = WarpRuntimeContextState.Disposing;
            if (activeDispatches == 0)
            {
                drained.Release();
            }
        }

        try
        {
            await lifetime.CancelAsync().ConfigureAwait(false);
            await drained.WaitAsync().ConfigureAwait(false);
            try
            {
                if (nativeProvider is not null)
                {
                    await nativeProvider.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                if (ownsJitCache)
                {
                    await jitCache.ShutdownOwnedAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            lock (sync)
            {
                state = WarpRuntimeContextState.Disposed;
                dispatchSlots.Dispose();
                drained.Dispose();
                lifetime.Dispose();
            }
        }
    }

    private WarpRuntimeEntry GetEntry(string entryIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryIdentity);
        if (!module.Entries.TryGetValue(entryIdentity, out WarpRuntimeEntry? entry))
        {
            throw new WarpHostException("WRPRUNTIME1004", $"The entry '{entryIdentity}' is not in the verified module.");
        }

        return entry;
    }

    private async Task<uint[]> DispatchAsync(WarpRuntimeEntry entry, IReadOnlyList<uint[]> inputs, IReadOnlyList<uint>? scalarArguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int count = ValidateArguments(entry, inputs, scalarArguments);
        (long bytes, CancellationToken contextToken) = ReserveDispatch(entry, count);
        bool slotHeld = false;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, contextToken);
            uint[][] snapshots = SnapshotInputs(inputs, entry.InputBufferCount, count, linked.Token);
            uint[] scalars = SnapshotScalars(scalarArguments, entry.ScalarArgumentCount, linked.Token);
            await dispatchSlots.WaitAsync(linked.Token).ConfigureAwait(false);
            slotHeld = true;
            linked.Token.ThrowIfCancellationRequested();
            lock (sync)
            {
                EnsureReady();
            }

            uint[] result = await ExecuteDispatchAsync(entry, snapshots, scalars, count, linked.Token).ConfigureAwait(false);
            lock (sync)
            {
                linked.Token.ThrowIfCancellationRequested();
                EnsureReady();
                return result;
            }
        }
        finally
        {
            ReleaseDispatch(bytes, slotHeld);
        }
    }

    private (long Bytes, CancellationToken ContextToken) ReserveDispatch(WarpRuntimeEntry entry, int count)
    {
        if (options.ExecutionQuantum < entry.Layout.MaximumBlockCost)
        {
            throw new WarpHostException("WRPRUNTIME1005", "The requested execution quantum cannot admit one verified basic block.");
        }

        long bytes = WarpDispatchResourceAdmission.EstimateBufferBytes(entry.Layout, count, options);
        lock (sync)
        {
            EnsureReady();
            jitCache.RequireReady();
            if (activeDispatches >= options.MaximumAdmittedDispatches)
            {
                throw new WarpHostException("WRPRUNTIME1005", "The context's running and queued dispatch admission limit is exhausted.");
            }

            if (bytes > options.MaximumBufferBytes - admittedBytes)
            {
                throw new WarpHostException("WRPRUNTIME1005", "The context cannot admit the dispatch buffer reservation.");
            }

            admittedBytes += bytes;
            activeDispatches++;
            return (bytes, lifetime.Token);
        }
    }

    private static uint[][] SnapshotInputs(IReadOnlyList<uint[]> inputs, int expectedInputs, int expectedLength, CancellationToken cancellationToken)
    {
        uint[][] snapshots = new uint[expectedInputs][];
        for (int index = 0; index < expectedInputs; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            uint[] input = inputs[index];
            if (input is null || input.Length != expectedLength)
            {
                throw new WarpHostException("WRPRUNTIME1004", "The input buffer shape changed after resource admission.");
            }

            snapshots[index] = (uint[])input.Clone();
        }

        return snapshots;
    }

    private static uint[] SnapshotScalars(IReadOnlyList<uint>? scalars, int expectedCount, CancellationToken cancellationToken)
    {
        if (expectedCount == 0)
        {
            return [];
        }

        uint[] snapshot = new uint[expectedCount];
        for (int index = 0; index < expectedCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            snapshot[index] = scalars![index];
        }

        return snapshot;
    }

    private async Task<uint[]> ExecuteDispatchAsync(WarpRuntimeEntry entry, uint[][] inputs, uint[] scalars, int count, CancellationToken cancellationToken)
    {
        if (nativeProvider is null)
        {
            CoreCLRResumableKernel compiled = await jitCache.GetOrCompileAsync(module, entry, cancellationToken).ConfigureAwait(false);
            return await Task.Run(() => Execute(entry, compiled, inputs, scalars, count, cancellationToken), cancellationToken).ConfigureAwait(false);
        }

        Native.IWarpNativeModule native = await nativeProvider.GetOrCompileAsync(entry, cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => ExecuteNative(entry, native, inputs, scalars, count, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch when (native.IsFaulted)
        {
            MarkFaulted();
            throw;
        }
    }

    private void ReleaseDispatch(long bytes, bool slotHeld)
    {
        if (slotHeld)
        {
            dispatchSlots.Release();
        }

        lock (sync)
        {
            admittedBytes -= bytes;
            activeDispatches--;
            if (activeDispatches == 0 && state == WarpRuntimeContextState.Disposing)
            {
                drained.Release();
            }
        }
    }

    private uint[] ExecuteNative(WarpRuntimeEntry entry, Native.IWarpNativeModule compiled, uint[][] inputs, uint[] scalars, int count, CancellationToken cancellationToken)
    {
        uint[] output = new uint[count];
        if (entry.Reduction.HasValue)
        {
            Native.WarpNativeReductionDispatch.ValidateAdmission(compiled.Image, count, entry.Reduction.Value);
        }

        int stride = entry.Layout.GetStateWords(options.MaximumCallDepth);
        int residentCount = Math.Min(count, options.MaximumResidentWorkers);
        uint[] states = CreateNativeBatchState(entry, residentCount, stride);
        using (Native.IWarpNativeMachineExecution execution = compiled.CreateMachineExecution(states, inputs, scalars, residentCount, 0, options.MaximumCallDepth))
        {
            for (int inputBase = 0; inputBase < count;)
            {
                int batchCount = Math.Min(count - inputBase, options.MaximumResidentWorkers);
                if (inputBase != 0)
                {
                    states = CreateNativeBatchState(entry, batchCount, stride);
                    cancellationToken.ThrowIfCancellationRequested();
                    execution.ResetBatch(states, batchCount, inputBase);
                }

                states = ResumeNativeBatch(entry, execution, inputBase, batchCount, stride, cancellationToken);
                CollectNativeResults(entry, states, inputBase, stride, output);
                inputBase += batchCount;
            }
        }

        if (!entry.Reduction.HasValue)
        {
            return output;
        }

        return [compiled.ReduceUInt32(output, entry.Reduction.Value, cancellationToken)];
    }

    private uint[] CreateNativeBatchState(WarpRuntimeEntry entry, int batchCount, int stride)
    {
        uint[] states = new uint[checked(batchCount * stride)];
        for (int worker = 0; worker < batchCount; worker++)
        {
            entry.Layout.ResetState(states.AsSpan(worker * stride, stride), options.MaximumStepsPerWorker);
        }

        return states;
    }

    private uint[] ResumeNativeBatch(WarpRuntimeEntry entry, Native.IWarpNativeMachineExecution execution, int inputBase,
        int batchCount, int stride, CancellationToken cancellationToken)
    {
        bool pending;
        uint[] states;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            states = execution.Resume(options.ExecutionQuantum, cancellationToken);
            if (states.Length != checked(batchCount * stride))
            {
                throw MalformedNativeState(entry, inputBase);
            }

            pending = false;
            for (int worker = 0; worker < batchCount; worker++)
            {
                uint status = states[worker * stride + WarpLogicalMachineLayout.StatusOffset];
                if (status > WarpLogicalMachineLayout.Faulted)
                {
                    throw MalformedNativeState(entry, inputBase + worker);
                }

                if (status == WarpLogicalMachineLayout.Runnable)
                {
                    pending = true;
                }
                else if (status == WarpLogicalMachineLayout.Faulted)
                {
                    MarkFaulted();
                }
            }
        }
        while (pending);

        return states;
    }

    private WarpRuntimeFaultException MalformedNativeState(WarpRuntimeEntry entry, int worker)
    {
        MarkFaulted();
        return new WarpRuntimeFaultException(entry.Identity, worker, WarpRuntimeFaultKind.RuntimeFailure,
            new InvalidOperationException("Native execution returned a malformed logical state; no output was published."));
    }

    private void CollectNativeResults(WarpRuntimeEntry entry, uint[] states, int inputBase, int stride, uint[] output)
    {
        for (int worker = 0; worker < states.Length / stride; worker++)
        {
            int offset = worker * stride;
            if (states[offset + WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Faulted)
            {
                MarkFaulted();
                throw CreateLogicalFault(entry, inputBase + worker, states.AsSpan(offset, stride));
            }

            output[inputBase + worker] = states[offset + WarpLogicalMachineLayout.ResultOffset];
        }
    }

    private uint[] Execute(WarpRuntimeEntry entry, CoreCLRResumableKernel compiled, uint[][] inputs, uint[] scalars, int count, CancellationToken cancellationToken)
    {
        uint[] output = new uint[count];
        WarpRuntimeFaultException? firstFault = null;
        var parallelOptions = new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = options.MaximumParallelWorkers };
        int residentCount = Math.Min(count, options.MaximumResidentWorkers);
        uint[][] states = new uint[residentCount][];
        WarpRuntimeFaultException?[] faults = new WarpRuntimeFaultException?[residentCount];
        foreach (ref uint[] logicalState in states.AsSpan())
        {
            logicalState = entry.Layout.CreateInitialState(options.MaximumCallDepth, options.MaximumStepsPerWorker);
        }

        for (int batchBase = 0; batchBase < count;)
        {
            int batchCount = Math.Min(count - batchBase, options.MaximumResidentWorkers);
            int inputBase = batchBase;
            Parallel.For(0, batchCount, parallelOptions, batchWorker =>
            {
                int worker = inputBase + batchWorker;
                uint[] logicalState = states[batchWorker];
                entry.Layout.ResetState(logicalState, options.MaximumStepsPerWorker);
                faults[batchWorker] = ExecuteWorker(entry, compiled, inputs, scalars, worker, logicalState, output, cancellationToken);
            });
            batchBase += batchCount;
            foreach (ref readonly WarpRuntimeFaultException? fault in faults.AsSpan(0, batchCount))
            {
                if (fault is not null)
                {
                    firstFault = fault;
                    break;
                }
            }

            if (firstFault is not null)
            {
                break;
            }
        }

        if (firstFault is not null)
        {
            MarkFaulted();
            throw firstFault;
        }

        if (!entry.Reduction.HasValue)
        {
            return output;
        }

        return [Reduce(entry.Reduction.Value, output, parallelOptions)];
    }

    private WarpRuntimeFaultException? ExecuteWorker(WarpRuntimeEntry entry, CoreCLRResumableKernel compiled, uint[][] inputs, uint[] scalars,
        int worker, uint[] logicalState, uint[] output, CancellationToken cancellationToken)
    {
        try
        {
            while (logicalState[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable)
            {
                compiled.ExecuteQuantum(inputs, scalars, worker, logicalState, options.MaximumCallDepth, options.ExecutionQuantum, cancellationToken);
            }

            if (logicalState[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Faulted)
            {
                MarkFaulted();
                return CreateLogicalFault(entry, worker, logicalState);
            }

            output[worker] = logicalState[WarpLogicalMachineLayout.ResultOffset];
            return null;
        }
        catch (CoreCLRResourceLimitException exception)
        {
            MarkFaulted();
            WarpRuntimeFaultKind kind = exception.Kind switch
            {
                CoreCLRResourceLimitKind.StepLimit => WarpRuntimeFaultKind.StepLimit,
                CoreCLRResourceLimitKind.CallDepth => WarpRuntimeFaultKind.CallDepth,
                _ => WarpRuntimeFaultKind.RuntimeFailure,
            };
            return new WarpRuntimeFaultException(entry.Identity, worker, kind, exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            MarkFaulted();
            return new WarpRuntimeFaultException(entry.Identity, worker, WarpRuntimeFaultKind.RuntimeFailure, exception);
        }
    }

    private void MarkFaulted()
    {
        lock (sync)
        {
            if (state == WarpRuntimeContextState.Ready)
            {
                state = WarpRuntimeContextState.Faulted;
            }
        }
    }

    private static WarpRuntimeFaultKind GetFaultKind(uint kind) => kind switch
    {
        WarpLogicalMachineLayout.StepLimitFault => WarpRuntimeFaultKind.StepLimit,
        WarpLogicalMachineLayout.CallDepthFault => WarpRuntimeFaultKind.CallDepth,
        _ => WarpRuntimeFaultKind.RuntimeFailure,
    };

    private static WarpRuntimeFaultException CreateLogicalFault(WarpRuntimeEntry entry, int worker, ReadOnlySpan<uint> logicalState)
    {
        WarpRuntimeFaultKind kind = GetFaultKind(logicalState[WarpLogicalMachineLayout.FaultKindOffset]);
        int function = checked((int)logicalState[WarpLogicalMachineLayout.FaultFunctionOffset]);
        int block = checked((int)logicalState[WarpLogicalMachineLayout.FaultBlockOffset]);
        string functionIdentity = function == 0 ? entry.Identity : entry.Kernel.Functions[function - 1].Name;
        ulong remaining = logicalState[WarpLogicalMachineLayout.RemainingStepsLowOffset] |
            ((ulong)logicalState[WarpLogicalMachineLayout.RemainingStepsHighOffset] << 32);
        return new WarpRuntimeFaultException(entry.Identity, worker, kind,
            new InvalidOperationException("The logical worker escaped at a portable resource boundary."), function, functionIdentity, block, remaining);
    }

    private static uint Reduce(WarpReductionOperation operation, uint[] values, ParallelOptions parallelOptions)
    {
        const int groupSize = 1024;
        uint identity = WarpReductionContract.GetDescriptor(operation).Identity;
        if (values.Length == 0)
        {
            return identity;
        }

        uint[] current = values;
        while (current.Length > 1)
        {
            int groups = checked((int)(((long)current.Length + groupSize - 1) / groupSize));
            uint[] next = new uint[groups];
            Parallel.For(0, groups, parallelOptions, group =>
            {
                int offset = group * groupSize;
                int length = Math.Min(groupSize, current.Length - offset);
                next[group] = WarpReductionContract.Reduce(operation, current.AsSpan(offset, length));
            });
            current = next;
        }

        return current[0];
    }

    private static int ValidateArguments(WarpRuntimeEntry entry, IReadOnlyList<uint[]> inputs, IReadOnlyList<uint>? scalarArguments)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count != entry.InputBufferCount || (scalarArguments?.Count ?? 0) != entry.ScalarArgumentCount)
        {
            throw new WarpHostException("WRPRUNTIME1004", "The dispatch argument counts do not match the verified entry.");
        }

        int? length = null;
        for (int index = 0; index < entry.InputBufferCount; index++)
        {
            uint[] input = inputs[index];
            if (input is null || (length.HasValue && input.Length != length.Value))
            {
                throw new WarpHostException("WRPRUNTIME1004", "All input buffers must be non-null and have the same length.");
            }

            length = input.Length;
        }

        return length ?? 0;
    }

    private void EnsureReady()
    {
        ObjectDisposedException.ThrowIf(state is WarpRuntimeContextState.Disposing or WarpRuntimeContextState.Disposed, this);
        if (state == WarpRuntimeContextState.Faulted)
        {
            throw new WarpHostException("WRPRUNTIME1006", "The context was invalidated by an escaped logical-worker fault.");
        }
    }
}
