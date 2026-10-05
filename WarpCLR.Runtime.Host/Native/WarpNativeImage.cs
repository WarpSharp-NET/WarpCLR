using System.Security.Cryptography;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host.Native;
internal sealed class WarpNativeImage
{
    public WarpNativeImage(
        WarpNativeTarget target,
        WarpNativeImageFormat format,
        string entryPoint,
        ReadOnlySpan<byte> content,
        string sourceHash,
        string toolchainIdentity,
        int inputBufferCount = 0,
        int scalarArgumentCount = 0,
        WarpLogicalMachineLayout? machineLayout = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolchainIdentity);
        ArgumentOutOfRangeException.ThrowIfNegative(inputBufferCount);
        ArgumentOutOfRangeException.ThrowIfNegative(scalarArgumentCount);
        ValidateEntryPoint(entryPoint, inputBufferCount, scalarArgumentCount, machineLayout);
        ValidateContent(target, format, content);

        Target = target;
        Format = format;
        EntryPoint = entryPoint;
        Content = content.ToArray();
        ContentHash = Convert.ToHexString(SHA256.HashData(Content.Span));
        SourceHash = sourceHash;
        ToolchainIdentity = toolchainIdentity;
        InputBufferCount = inputBufferCount;
        ScalarArgumentCount = scalarArgumentCount;
        MachineLayout = machineLayout;
        if (machineLayout is not null) { WarpNativeAtomicAdmission.Validate(machineLayout, target); }
        ConformanceStatus = WarpConformanceStatus.DevelopmentNonconforming;
    }

    private static void ValidateEntryPoint(string entryPoint, int inputBufferCount, int scalarArgumentCount, WarpLogicalMachineLayout? machineLayout)
    {
        if (entryPoint is not WarpDeviceAbi.IntegerMapEntryPoint and not WarpDeviceAbi.IntegerReductionEntryPoint &&
            !(string.Equals(entryPoint, "warp_resume", StringComparison.Ordinal) && machineLayout is not null))
        {
            throw new ArgumentException("The native entry point does not implement the registered UInt32 ABI.", nameof(entryPoint));
        }

        if (machineLayout is not null &&
            (!string.Equals(entryPoint, "warp_resume", StringComparison.Ordinal) ||
             inputBufferCount != machineLayout.Kernel.InputBufferCount || scalarArgumentCount != machineLayout.Kernel.ScalarArgumentCount))
        {
            throw new ArgumentException("The native machine entry point must match its verified logical layout.", nameof(machineLayout));
        }
    }

    private static void ValidateContent(WarpNativeTarget target, WarpNativeImageFormat format, ReadOnlySpan<byte> content)
    {
        WarpNativeImageFormat expected = target.Backend switch
        {
            WarpBackendKind.NVPTX => WarpNativeImageFormat.Ptx,
            WarpBackendKind.AMDGPU => WarpNativeImageFormat.Hsaco,
            WarpBackendKind.SPIRV => WarpNativeImageFormat.SpirV,
            _ => throw new ArgumentException("The native backend is invalid.", nameof(target)),
        };
        if (format != expected || content.IsEmpty)
        {
            throw new ArgumentException("The native image format or content is invalid.", nameof(content));
        }

        if (format == WarpNativeImageFormat.Hsaco &&
            (content.Length < 64 || content[0] != 0x7f || content[1] != 'E' || content[2] != 'L' || content[3] != 'F' ||
             content[4] != 2 || content[5] != 1 || content[18] != 0xe0 || content[19] != 0))
        {
            throw new ArgumentException("A HSACO must be a little-endian ELF64 AMDGPU image, not LLVM text.", nameof(content));
        }

        if (format == WarpNativeImageFormat.SpirV &&
            (content.Length < 20 || content.Length % sizeof(uint) != 0 ||
             content[0] != 3 || content[1] != 2 || content[2] != 0x23 || content[3] != 7))
        {
            throw new ArgumentException("A SPIR-V module must contain binary words, not LLVM text.", nameof(content));
        }

    }

    public WarpNativeTarget Target { get; }
    public WarpNativeImageFormat Format { get; }
    public string EntryPoint { get; }
    public ReadOnlyMemory<byte> Content { get; }
    public string ContentHash { get; }
    public string SourceHash { get; }
    public string ToolchainIdentity { get; }
    public int InputBufferCount { get; }
    public int ScalarArgumentCount { get; }
    public WarpLogicalMachineLayout? MachineLayout { get; }
    public bool IsReduction => string.Equals(EntryPoint, WarpDeviceAbi.IntegerReductionEntryPoint, StringComparison.Ordinal);
    public string DeviceAbiVersion => MachineLayout is null ? WarpDeviceAbi.Version : WarpLogicalMachineLayout.Version;
    public bool SupportsPortableSafepoints => MachineLayout is not null;
    public bool SupportsScalableReduction => MachineLayout is not null;
    public WarpConformanceStatus ConformanceStatus { get; }

    public void RequireProductionAdmission()
    {
        if (!SupportsPortableSafepoints)
        {
            throw new WarpHostException("WRPNATIVE1008",
                "This native image has not implemented the portable control/fault ABI and is not production-admissible.");
        }
    }

    public void ValidateArguments(IReadOnlyList<uint[]> inputs, IReadOnlyList<uint> scalars, bool reduction)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(scalars);
        if (MachineLayout is not null || inputs.Count != InputBufferCount || scalars.Count != ScalarArgumentCount || reduction != IsReduction)
        {
            throw new WarpHostException("WRPNATIVE1004", "The arguments do not match the loaded native entry-point ABI.");
        }
    }
}
