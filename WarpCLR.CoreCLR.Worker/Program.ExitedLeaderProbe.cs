using System.Text;
using WarpCLR.Backend.CoreCLR;

namespace WarpCLR.CoreCLR.Worker;

internal static partial class Program
{
    private static async Task ExitLeaderWithInheritedPipeAsync(Stream input, Stream output, byte[] key)
    {
        WarpCoreCLRWorkerProtocol.Frame frame = await WarpCoreCLRWorkerProtocol.ReadAsync(input, key, 1, CancellationToken.None).ConfigureAwait(false);
        if (frame.Kind != WarpCoreCLRWorkerProtocol.Compile || frame.Payload.Length < 64)
        { throw new InvalidDataException("Exited-leader containment probe requires an actual admitted plan."); }
        string hash = Encoding.ASCII.GetString(frame.Payload, 0, 64);
        WarpCoreCLRBinaryPlanCodec.Deserialize(frame.Payload.AsSpan(64), hash);
        await WarpCoreCLRWorkerProtocol.WriteAsync(output, key, 8, 1, [], CancellationToken.None).ConfigureAwait(false);
        await StartInheritedPipeProbeAsync(output, key).ConfigureAwait(false);
    }
}
