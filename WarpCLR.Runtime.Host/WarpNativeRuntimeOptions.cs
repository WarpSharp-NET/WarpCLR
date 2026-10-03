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

    internal static void Validate(WarpNativeRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegative(options.DeviceOrdinal, nameof(options));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumCachedModules, nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.LlvmAssemblerPath, nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.LlvmCodeGeneratorPath, nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.LlvmLinkerPath, nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.SpirVTranslatorPath, nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.SpirVValidatorPath, nameof(options));
        if (options.DriverLibraryPath is not null && !Path.IsPathFullyQualified(options.DriverLibraryPath))
        {
            throw new ArgumentException("An explicit driver library requires an absolute path.", nameof(options));
        }

        if (options.CompilationTimeout <= TimeSpan.Zero || options.CompilationTimeout > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.CompilationTimeout, "Native compilation timeout must be positive and finite.");
        }
    }
}
