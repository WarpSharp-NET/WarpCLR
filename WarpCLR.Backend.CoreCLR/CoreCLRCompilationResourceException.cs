using System.Diagnostics.CodeAnalysis;

namespace WarpCLR.Backend.CoreCLR;

[SuppressMessage("Design", "CA1032:Implement standard exception constructors",
    Justification = "Compilation admission failures require a body identity and structured resource limits.")]
[SuppressMessage("Roslynator", "RCS1194:Implement exception constructors",
    Justification = "Compilation admission failures require a body identity and structured resource limits.")]
public sealed class CoreCLRCompilationResourceException : Exception
{
    public CoreCLRCompilationResourceException(string bodyName, long estimatedFrameBytes, long frameLimitBytes)
        : base($"CoreCLR rejected '{bodyName}' before compilation: conservative native frame estimate " +
            $"{estimatedFrameBytes} bytes exceeds the admitted per-method limit of {frameLimitBytes} bytes.")
    {
        BodyName = bodyName;
        EstimatedFrameBytes = estimatedFrameBytes;
        FrameLimitBytes = frameLimitBytes;
    }

    public string BodyName { get; }

    public long EstimatedFrameBytes { get; }

    public long FrameLimitBytes { get; }
}
