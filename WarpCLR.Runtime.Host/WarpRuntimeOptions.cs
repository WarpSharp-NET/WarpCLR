using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

public sealed record WarpRuntimeOptions
{
    public long MaximumBufferBytes { get; init; } = 512L * 1024 * 1024;

    public long MaximumStepsPerWorker { get; init; } = WarpRuntimeAbi.DefaultMaximumStepsPerWorker;

    public int MaximumCallDepth { get; init; } = WarpRuntimeAbi.DefaultMaximumCallDepth;

    public int MaximumParallelWorkers { get; init; } = Environment.ProcessorCount;

    public int MaximumConcurrentDispatches { get; init; } = 4;

    public int ExecutionQuantum { get; init; } = 4096;

    public int MaximumResidentWorkers { get; init; } = 256;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumBufferBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumStepsPerWorker);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumCallDepth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumCallDepth, WarpRuntimeAbi.DefaultMaximumCallDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumParallelWorkers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumConcurrentDispatches);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ExecutionQuantum);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumResidentWorkers);
    }
}

public enum WarpRuntimeContextState
{
    Ready,
    Faulted,
    Disposing,
    Disposed,
}

public enum WarpRuntimeFaultKind
{
    StepLimit,
    CallDepth,
    RuntimeFailure,
}

public sealed class WarpRuntimeFaultException : Exception
{
    internal WarpRuntimeFaultException(string entryIdentity, int workerIndex, WarpRuntimeFaultKind kind, Exception innerException)
        : base($"Entry '{entryIdentity}' failed at logical worker {workerIndex}: {kind}.", innerException)
    {
        EntryIdentity = entryIdentity;
        WorkerIndex = workerIndex;
        Kind = kind;
    }

    public string EntryIdentity { get; }

    public int WorkerIndex { get; }

    public WarpRuntimeFaultKind Kind { get; }
}
