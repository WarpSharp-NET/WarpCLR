using System.Text;

namespace WarpCLR.Runtime.Host.Native;

internal sealed class WarpToolProcessCapture
{
    private const int MaximumCharacters = 65536;
    private readonly Lock gate = new();
    private readonly StringBuilder text = new();
    private bool truncated;
    private bool endOfStream;

    public bool EndOfStream
    {
        get { lock (gate) { return endOfStream; } }
    }

    public string Snapshot()
    {
        lock (gate)
        {
            return text.ToString() + (truncated ? Environment.NewLine + "[diagnostic output truncated]" : string.Empty);
        }
    }

    public async Task DrainAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        char[] buffer = new char[4096];
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
            {
                lock (gate)
                {
                    int available = MaximumCharacters - text.Length;
                    text.Append(buffer, 0, Math.Min(count, available));
                    truncated |= count > available;
                }
            }

            lock (gate) { endOfStream = true; }
        }
        catch (Exception error) when (cancellationToken.IsCancellationRequested &&
            error is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Preserve the captured prefix. Stopping a reader is not evidence of EOF.
        }
    }
}
