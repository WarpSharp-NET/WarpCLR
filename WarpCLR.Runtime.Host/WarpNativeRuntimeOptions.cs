namespace WarpCLR.Runtime.Host;

public sealed record WarpNativeRuntimeOptions
{
    public int DeviceOrdinal { get; init; }

    public string? DriverLibraryPath { get; init; }

    public string LlvmAssemblerPath { get; init; } = "llvm-as-19";

    public string LlvmCodeGeneratorPath { get; init; } = "llc-19";

    public string LlvmLinkerPath { get; init; } = "ld.lld-19";

    public string SpirVTranslatorPath { get; init; } = "llvm-spirv-19";

    public string SpirVValidatorPath { get; init; } = "spirv-val";

    public TimeSpan CompilationTimeout { get; init; } = TimeSpan.FromMinutes(2);

    public int MaximumCachedModules { get; init; } = 32;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(DeviceOrdinal);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumCachedModules);
        ArgumentException.ThrowIfNullOrWhiteSpace(LlvmAssemblerPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(LlvmCodeGeneratorPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(LlvmLinkerPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(SpirVTranslatorPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(SpirVValidatorPath);
        if (DriverLibraryPath is not null && !Path.IsPathFullyQualified(DriverLibraryPath))
        {
            throw new ArgumentException("An explicit driver library requires an absolute path.", nameof(DriverLibraryPath));
        }

        if (CompilationTimeout <= TimeSpan.Zero || CompilationTimeout > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(CompilationTimeout));
        }
    }
}
