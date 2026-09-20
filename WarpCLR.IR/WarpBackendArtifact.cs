using System.Security.Cryptography;
using System.Text;

namespace WarpCLR.IR;

public enum WarpArtifactFormat
{
    CoreCLRPlan,
    NVPTX,
    AMDGPULLVMIR,
    SPIRVLLVMIR,
}

public enum WarpConformanceStatus
{
    DevelopmentNonconforming,
}

public static class WarpArtifactFormatCatalog
{
    public static WarpArtifactFormat ForBackend(WarpBackendKind backend) => backend switch
    {
        WarpBackendKind.CoreCLR => WarpArtifactFormat.CoreCLRPlan,
        WarpBackendKind.NVPTX => WarpArtifactFormat.NVPTX,
        WarpBackendKind.AMDGPU => WarpArtifactFormat.AMDGPULLVMIR,
        WarpBackendKind.SPIRV => WarpArtifactFormat.SPIRVLLVMIR,
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
        ContentHash = Convert.ToHexString(SHA256.HashData(Content.Span));
        ConformanceStatus = WarpConformanceStatus.DevelopmentNonconforming;
    }

    public WarpBackendKind Backend { get; }

    public WarpArtifactFormat Format { get; }

    public string EntryPoint { get; }

    public WarpConformanceStatus ConformanceStatus { get; }

    public ReadOnlyMemory<byte> Content { get; }

    public string ContentHash { get; }

    public string GetText() => Encoding.UTF8.GetString(Content.Span);
}
