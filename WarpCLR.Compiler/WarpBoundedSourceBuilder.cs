using System.Text;
using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal sealed class WarpBoundedSourceBuilder
{
    private static readonly int NewLineBytes = Encoding.UTF8.GetByteCount(Environment.NewLine);
    private readonly string identity;
    private readonly int maximumBytes;
    private readonly StringBuilder source;
    private readonly CancellationToken cancellationToken;
    private int bytes;

    public WarpBoundedSourceBuilder(string identity, int maximumBytes, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        this.identity = identity;
        this.maximumBytes = Math.Min(maximumBytes, WarpCompilationAdmission.MaximumSourceBytes);
        this.cancellationToken = cancellationToken;
        source = new StringBuilder(Math.Min(4096, this.maximumBytes), this.maximumBytes);
    }

    public void AppendLine(string line)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long requested = bytes + (long)Encoding.UTF8.GetByteCount(line) + NewLineBytes;
        if (requested > maximumBytes)
        {
            throw new WarpCompilationResourceException(identity, WarpCompilationResourceKind.SourceBytes, requested, maximumBytes);
        }

        source.AppendLine(line);
        bytes = checked((int)requested);
    }

    public override string ToString() => source.ToString();
}
