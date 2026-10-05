namespace WarpCLR.Runtime.Host;

public sealed record WarpCoreCLRWorkerOptions
{
    public int MaximumWorkerProcesses { get; init; } = 256;
    public string? DotnetHostPath { get; init; }
    public string? WorkerAssemblyPath { get; init; }
    public TimeSpan CompilationTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan QuantumTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan CleanupTimeout { get; init; } = TimeSpan.FromSeconds(2);

    internal static void Validate(WarpCoreCLRWorkerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumWorkerProcesses, nameof(options));
        ValidatePath(options.DotnetHostPath); ValidatePath(options.WorkerAssemblyPath);
        ValidateTime(options.CompilationTimeout); ValidateTime(options.QuantumTimeout); ValidateTime(options.CleanupTimeout);
    }

    private static void ValidatePath(string? path)
    {
        if (path is null) { return; }
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path)) { throw new ArgumentException("CoreCLR worker deployment paths must be absolute.", nameof(path)); }
    }

    private static void ValidateTime(TimeSpan time)
    {
        if (time <= TimeSpan.Zero || time.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(time), "Worker deadlines must be finite positive timer durations.");
        }
    }
}
