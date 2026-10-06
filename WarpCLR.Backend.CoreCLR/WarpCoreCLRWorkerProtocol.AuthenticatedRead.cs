using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace WarpCLR.Backend.CoreCLR;

internal static partial class WarpCoreCLRWorkerProtocol
{
    internal const string AuthenticatedReadSemantics = "warp.coreclr.worker/read-receipt-exact-frame-session-key-kind-sequence-complete-payload/0.1";
    private static readonly object ReadReceiptIssuer = new();
    private static readonly ConditionalWeakTable<Frame, AuthenticatedReadReceipt> AuthenticatedReads = new();

    // ReadAsync alone populates the table after its existing HMAC and wire
    // guards. This grants envelope authenticity only, never source permission.
    // Return an independently copied, digest-checked payload so a consumer does
    // not authenticate the public mutable byte array and then read changed bytes.
    // Callers must separately provide every source/operation authority decision.
    internal static byte[] RequireAuthenticatedFrame(Frame frame, byte[] key, ushort kind, ulong sequence)
    {
        ArgumentNullException.ThrowIfNull(frame); ArgumentNullException.ThrowIfNull(key);
        if (!AuthenticatedReads.TryGetValue(frame, out AuthenticatedReadReceipt? receipt))
        {
            throw new InvalidDataException("The worker frame was not issued by an authenticated read.");
        }
        return receipt.Require(frame, key, kind, sequence);
    }

    private sealed class AuthenticatedReadReceipt
    {
        private readonly Frame frame;
        private readonly byte[] key;
        private readonly byte[] keyIdentity;
        private readonly byte[] payloadIdentity;
        private readonly ushort kind;
        private readonly ulong sequence;

        private AuthenticatedReadReceipt(object issuer, Frame frame, byte[] key, byte[] authenticatedKey, ushort kind, ulong sequence)
        {
            // Check the private issuer before capturing any receipt data.
            if (!ReferenceEquals(issuer, ReadReceiptIssuer))
            {
                throw new InvalidDataException("An authenticated read receipt requires its private runtime issuer.");
            }
            this.frame = frame; this.key = key; this.kind = kind; this.sequence = sequence;
            keyIdentity = SHA256.HashData(authenticatedKey); payloadIdentity = SHA256.HashData(frame.Payload);
        }

        internal static AuthenticatedReadReceipt Issue(object issuer, Frame frame, byte[] key, byte[] authenticatedKey, ushort kind, ulong sequence) =>
            new(issuer, frame, key, authenticatedKey, kind, sequence);

        internal byte[] Require(Frame candidate, byte[] sessionKey, ushort expectedKind, ulong expectedSequence)
        {
            if (!ReferenceEquals(candidate, frame) || !ReferenceEquals(sessionKey, key) || kind != expectedKind ||
                candidate.Kind != kind || sequence != expectedSequence)
            {
                throw new InvalidDataException("The authenticated worker frame identity, key, kind, or sequence differs from its read receipt.");
            }
            byte[] keySnapshot = (byte[])sessionKey.Clone(); byte[] payloadSnapshot = (byte[])candidate.Payload.Clone();
            if (!CryptographicOperations.FixedTimeEquals(keyIdentity, SHA256.HashData(keySnapshot)) ||
                !CryptographicOperations.FixedTimeEquals(payloadIdentity, SHA256.HashData(payloadSnapshot)))
            {
                throw new InvalidDataException("The authenticated worker frame key or complete payload has changed.");
            }
            return payloadSnapshot;
        }
    }
}
