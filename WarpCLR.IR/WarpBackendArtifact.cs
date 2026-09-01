using System.Text;

namespace WarpCLR.IR;

public enum WarpArtifactFormat
{
    CpuExpressionPlan,
    NvidiaPtx,
    AmdLlvmIr,
    IntelSpirvLlvmIr,
}

public enum WarpConformanceStatus
{
    DevelopmentNonconforming,
}

public static class WarpArtifactFormatCatalog
{
    public static WarpArtifactFormat ForBackend(WarpBackendKind backend) => backend switch
    {
        WarpBackendKind.CpuReference => WarpArtifactFormat.CpuExpressionPlan,
        WarpBackendKind.Nvidia => WarpArtifactFormat.NvidiaPtx,
        WarpBackendKind.Amd => WarpArtifactFormat.AmdLlvmIr,
        WarpBackendKind.Intel => WarpArtifactFormat.IntelSpirvLlvmIr,
        _ => throw new ArgumentOutOfRangeException(
            nameof(backend),
            backend,
            "The backend is not registered."),
    };
}

public sealed class WarpBackendArtifact
{
    public WarpBackendArtifact(
        WarpBackendKind backend,
        WarpArtifactFormat format,
        string entryPoint,
        ReadOnlySpan<byte> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPoint);

        if (format != WarpArtifactFormatCatalog.ForBackend(backend))
        {
            throw new ArgumentException(
                $"The {format} artifact format does not match the {backend} backend.",
                nameof(format));
        }

        if (content.IsEmpty)
        {
            throw new ArgumentException("The backend artifact cannot be empty.", nameof(content));
        }

        Backend = backend;
        Format = format;
        EntryPoint = entryPoint;
        Content = content.ToArray();
        ConformanceStatus = WarpConformanceStatus.DevelopmentNonconforming;
    }

    public WarpBackendKind Backend { get; }

    public WarpArtifactFormat Format { get; }

    public string EntryPoint { get; }

    public WarpConformanceStatus ConformanceStatus { get; }

    public ReadOnlyMemory<byte> Content { get; }

    public string GetText() => Encoding.UTF8.GetString(Content.Span);
}
