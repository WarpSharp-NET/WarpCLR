namespace WarpCLR.Runtime.Host;

public sealed record WarpJitCacheOptions
{
    public int MaximumMemoryEntries { get; init; } = 128;

    public int MaximumConcurrentCompilations { get; init; } = 2;

    public string? DirectoryPath { get; init; }

    public long MaximumDiskBytes { get; init; } = 256L * 1024 * 1024;

    public int MaximumDiskEntries { get; init; } = 1024;

    internal static void Validate(WarpJitCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumMemoryEntries, nameof(options));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumConcurrentCompilations, nameof(options));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumDiskBytes, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaximumDiskEntries, 1, nameof(options));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaximumDiskEntries, 65536, nameof(options));
        if (options.DirectoryPath is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(options.DirectoryPath, nameof(options));
            if (!Path.IsPathFullyQualified(options.DirectoryPath))
            {
                throw new ArgumentException("The JIT cache requires an explicit absolute directory.", nameof(options));
            }
        }
    }
}
