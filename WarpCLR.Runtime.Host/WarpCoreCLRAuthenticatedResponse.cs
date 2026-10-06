using WarpCLR.Backend.CoreCLR;

namespace WarpCLR.Runtime.Host;

// Transport provenance only. This cannot grant Source execution or a controller role.
internal sealed class WarpCoreCLRAuthenticatedResponse
{
    internal const string Semantics =
        "warp.coreclr.host/retained-exact-read-frame-session-kind-sequence-payload-transport-only/0.1";

    // A strong reference to the exact Frame retains its private protocol CWT
    // receipt. Neither a public Frame constructor nor a record clone has that receipt.
    private readonly WarpCoreCLRWorkerProtocol.Frame frame;
    private readonly byte[] sessionKey;
    private readonly ushort kind;
    private readonly ulong sequence;

    private WarpCoreCLRAuthenticatedResponse(WarpCoreCLRWorkerProtocol.Frame frame, byte[] sessionKey, ushort kind, ulong sequence)
    {
        this.frame = frame;
        this.sessionKey = sessionKey;
        this.kind = kind;
        this.sequence = sequence;
    }

    internal static WarpCoreCLRAuthenticatedResponse Capture(WarpCoreCLRWorkerProtocol.Frame frame, byte[] sessionKey,
        ushort kind, ulong sequence, out byte[] verifiedPayload)
    {
        // Reuse the actual ReadAsync-issued receipt before constructing this holder.
        // The returned copy is the exact snapshot consumers must parse.
        verifiedPayload = WarpCoreCLRWorkerProtocol.RequireAuthenticatedFrame(frame, sessionKey, kind, sequence);
        return new(frame, sessionKey, kind, sequence);
    }

    internal byte[] RequirePayload(byte[] exactSessionKey, ushort expectedKind, ulong expectedSequence)
    {
        if (!ReferenceEquals(sessionKey, exactSessionKey) || kind != expectedKind || sequence != expectedSequence)
        { throw new InvalidDataException("The retained authenticated response belongs to another session, kind or sequence."); }
        return WarpCoreCLRWorkerProtocol.RequireAuthenticatedFrame(frame, exactSessionKey, expectedKind, expectedSequence);
    }
}
