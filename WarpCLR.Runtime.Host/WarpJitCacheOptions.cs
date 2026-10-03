namespace WarpCLR.Runtime.Host;

public sealed record WarpJitCacheOptions
{
    public int MaximumMemoryEntries { get; init; } = 128;

    public int MaximumConcurrentCompilations { get; init; } = 2;

    public string? DirectoryPath { get; init; }

    public long MaximumDiskBytes { get; init; } = 256L * 1024 * 1024;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumMemoryEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumConcurrentCompilations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumDiskBytes);
        if (DirectoryPath is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(DirectoryPath);
            if (!Path.IsPathFullyQualified(DirectoryPath))
            {
                throw new ArgumentException("The JIT cache requires an explicit absolute directory.", nameof(DirectoryPath));
            }
        }
    }
}

public readonly record struct WarpJitCacheStatistics(long CompilationCount, long MemoryHitCount, long DiskHitCount, int MemoryEntryCount);
