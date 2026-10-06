using System.Buffers.Binary;
using System.Security.Cryptography;

namespace WarpCLR.Backend.CoreCLR;

internal static partial class WarpCoreCLRWorkerProtocol
{
    internal const string Version = "warp.coreclr.worker/authenticated-native-jit-owned-alias-managed-terminal-logical-worker-pair-atomics/0.3";
    internal const int MaximumPayloadBytes = WarpCoreCLRBinaryPlanCodec.MaximumBytes + 65536;
    internal const ushort Hello = 1;
    internal const ushort Compile = 2;
    internal const ushort Execute = 3;
    internal const ushort Compiled = 4;
    internal const ushort Executed = 5;
    internal const ushort JitEntering = 6;
    internal const ushort BindInputs = 9;
    internal const ushort InputsBound = 10;
    internal const ushort ExecuteBatch = 11;
    internal const ushort BatchExecuted = 12;
    internal const ushort UnbindInputs = 13;
    internal const ushort InputsUnbound = 14;
    private const uint Magic = 0x57525031;
    private const int HeaderBytes = 20;
    private const int MacBytes = 32;

    internal static async Task WriteAsync(Stream stream, byte[] key, ushort kind, ulong sequence,
        byte[] payload, CancellationToken cancellationToken)
    {
        if (payload.Length > MaximumPayloadBytes) { throw new InvalidDataException("Worker frame exceeds admission."); }
        byte[] header = new byte[HeaderBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(header, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), kind);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(8), sequence);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16), payload.Length);
        byte[] mac = Authenticate(key, header, payload);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(mac, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<Frame> ReadAsync(Stream stream, byte[] key, ulong sequence, CancellationToken cancellationToken)
    {
        byte[] header = new byte[HeaderBytes];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(16));
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != Magic ||
            BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(4)) != 1 ||
            BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(8)) != sequence || length < 0 || length > MaximumPayloadBytes)
        {
            throw new InvalidDataException("Worker frame identity, sequence, or length is invalid.");
        }
        byte[] mac = new byte[MacBytes];
        await stream.ReadExactlyAsync(mac, cancellationToken).ConfigureAwait(false);
        byte[] payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        // Bind the receipt to the exact bytes used for HMAC verification even
        // if a caller subsequently changes its mutable session-key array.
        byte[] authenticatedKey = (byte[])key.Clone();
        if (!CryptographicOperations.FixedTimeEquals(mac, Authenticate(authenticatedKey, header, payload)))
        {
            throw new InvalidDataException("Worker frame authentication failed.");
        }
        ushort kind = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6));
        var frame = new Frame(kind, payload);
        AuthenticatedReads.Add(frame, AuthenticatedReadReceipt.Issue(ReadReceiptIssuer, frame, key, authenticatedKey, kind, sequence));
        return frame;
    }

    private static byte[] Authenticate(byte[] key, byte[] header, byte[] payload)
    {
        using var hash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
        hash.AppendData(header); hash.AppendData(payload);
        return hash.GetHashAndReset();
    }

    internal sealed record Frame(ushort Kind, byte[] Payload);
}
