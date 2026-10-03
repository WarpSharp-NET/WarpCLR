using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

public sealed class WarpRuntimeContext : IAsyncDisposable
{
    private readonly object sync = new();
    private readonly WarpRuntimeModule module;
    private readonly WarpRuntimeOptions options;
    private readonly WarpJitCache jitCache;
    private readonly SemaphoreSlim dispatchSlots;
    private readonly CancellationTokenSource lifetime = new();
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private WarpRuntimeContextState state;
    private int activeDispatches;
    private long admittedBytes;
    private Task? disposal;

    public WarpRuntimeContext(WarpRuntimeModule module, WarpBackendKind backend, WarpRuntimeOptions? options = null, WarpJitCache? jitCache = null)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (backend != WarpBackendKind.CoreCLR)
        {
            throw new WarpHostException("WRPRUNTIME1003", "The selected device has no admitted production execution provider. No backend fallback is permitted.");
        }

        this.module = module;
        this.options = options ?? new WarpRuntimeOptions();
        this.options.Validate();
        this.jitCache = jitCache ?? new WarpJitCache();
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
        WarpRuntimeEntry entry = GetEntry(entryIdentity);
        if (entry.Reduction.HasValue)
        {
            throw new WarpHostException("WRPRUNTIME1004", "The requested entry is a reduction, not a map.");
        }

        return DispatchAsync(entry, inputs, scalarArguments, cancellationToken);
    }

    public async Task<uint> DispatchUInt32ReductionAsync(string entryIdentity, IReadOnlyList<uint[]> inputs, IReadOnlyList<uint>? scalarArguments = null, CancellationToken cancellationToken = default)
    {
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
                drained.TrySetResult();
            }
        }

        await lifetime.CancelAsync().ConfigureAwait(false);
        await drained.Task.ConfigureAwait(false);
        lock (sync)
        {
            if (state != WarpRuntimeContextState.Disposed)
            {
                state = WarpRuntimeContextState.Disposed;
                dispatchSlots.Dispose();
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
        long reductionWords = entry.Reduction.HasValue ? ((long)count + 1023) / 1024 * 2 : 0;
        long bytes = checked(((long)count * (entry.InputBufferCount + 1) + reductionWords + entry.ScalarArgumentCount) * sizeof(uint));
        CancellationToken contextToken;
        lock (sync)
        {
            EnsureReady();
            if (bytes > options.MaximumBufferBytes - admittedBytes)
            {
                throw new WarpHostException("WRPRUNTIME1005", "The context cannot admit the dispatch buffer reservation.");
            }

            admittedBytes += bytes;
            activeDispatches++;
            contextToken = lifetime.Token;
        }

        bool slotHeld = false;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, contextToken);
        try
        {
            await dispatchSlots.WaitAsync(linked.Token).ConfigureAwait(false);
            slotHeld = true;
            uint[][] snapshots = new uint[inputs.Count][];
            for (int index = 0; index < inputs.Count; index++)
            {
                snapshots[index] = (uint[])inputs[index].Clone();
            }

            uint[] scalars = scalarArguments?.ToArray() ?? [];
            CoreCLRJitKernel compiled = await jitCache.GetOrCompileAsync(module, entry, linked.Token).ConfigureAwait(false);
            uint[] result = await Task.Run(() => Execute(entry, compiled, snapshots, scalars, count, linked.Token), linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            lock (sync)
            {
                EnsureReady();
                return result;
            }
        }
        finally
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
                    drained.TrySetResult();
                }
            }
        }
    }

    private uint[] Execute(WarpRuntimeEntry entry, CoreCLRJitKernel compiled, uint[][] inputs, uint[] scalars, int count, CancellationToken cancellationToken)
    {
        uint[] output = new uint[count];
        WarpRuntimeFaultException? firstFault = null;
        object faultSync = new();
        var parallelOptions = new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = options.MaximumParallelWorkers };
        Parallel.For(0, count, parallelOptions,
            () => new CoreCLRExecutionBudget(options.MaximumStepsPerWorker, options.MaximumCallDepth, cancellationToken),
            (worker, _, budget) =>
            {
                budget.Reset();
                try
                {
                    output[worker] = compiled.Invoke(inputs, scalars, worker, budget);
                }
                catch (CoreCLRResourceLimitException exception)
                {
                    WarpRuntimeFaultKind kind = exception.Kind == CoreCLRResourceLimitKind.StepLimit ? WarpRuntimeFaultKind.StepLimit : WarpRuntimeFaultKind.CallDepth;
                    var fault = new WarpRuntimeFaultException(entry.Identity, worker, kind, exception);
                    lock (faultSync)
                    {
                        if (firstFault is null || worker < firstFault.WorkerIndex)
                        {
                            firstFault = fault;
                        }
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    var fault = new WarpRuntimeFaultException(entry.Identity, worker, WarpRuntimeFaultKind.RuntimeFailure, exception);
                    lock (faultSync)
                    {
                        if (firstFault is null || worker < firstFault.WorkerIndex)
                        {
                            firstFault = fault;
                        }
                    }
                }

                return budget;
            },
            _ => { });

        if (firstFault is not null)
        {
            lock (sync)
            {
                if (state == WarpRuntimeContextState.Ready)
                {
                    state = WarpRuntimeContextState.Faulted;
                }
            }

            throw firstFault;
        }

        if (!entry.Reduction.HasValue)
        {
            return output;
        }

        return [Reduce(entry.Reduction.Value, output, parallelOptions)];
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
        foreach (uint[] input in inputs)
        {
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
