namespace WarpCLR.Runtime.Host.Native;

internal sealed record WarpNativeToolchainOptions
{
    public string LlvmAssembler { get; init; } = "llvm-as-19";
    public string LlvmCodeGenerator { get; init; } = "llc-19";
    public string LlvmLinker { get; init; } = "ld.lld-19";
    public string SpirVTranslator { get; init; } = "llvm-spirv-19";
    public string SpirVValidator { get; init; } = "spirv-val";
    public TimeSpan ProcessTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public int MaximumSourceBytes { get; init; } = 64 * 1024 * 1024;
    public int MaximumImageBytes { get; init; } = 64 * 1024 * 1024;
}
