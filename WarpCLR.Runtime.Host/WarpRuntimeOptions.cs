using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

public sealed record WarpRuntimeOptions
{
    public long MaximumBufferBytes { get; init; } = 512L * 1024 * 1024;

    public long MaximumStepsPerWorker { get; init; } = WarpRuntimeAbi.DefaultMaximumStepsPerWorker;

    public int MaximumCallDepth { get; init; } = WarpRuntimeAbi.DefaultMaximumCallDepth;

    public int MaximumParallelWorkers { get; init; } = Environment.ProcessorCount;

    public int MaximumConcurrentDispatches { get; init; } = 4;

    public int MaximumAdmittedDispatches { get; init; } = 128;

    public int ExecutionQuantum { get; init; } = 4096;

    public int MaximumResidentWorkers { get; init; } = 256;

    public WarpNativeRuntimeOptions Native { get; init; } = new();

    internal static void Validate(WarpRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumBufferBytes, nameof(options));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumStepsPerWorker, nameof(options));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumCallDepth, nameof(options));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaximumCallDepth, WarpRuntimeAbi.DefaultMaximumCallDepth, nameof(options));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumParallelWorkers, nameof(options));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumConcurrentDispatches, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaximumAdmittedDispatches, options.MaximumConcurrentDispatches, nameof(options));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.ExecutionQuantum, nameof(options));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumResidentWorkers, nameof(options));
        ArgumentNullException.ThrowIfNull(options.Native, nameof(options));
        WarpNativeRuntimeOptions.Validate(options.Native);
    }
}
