using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using WarpCLR.Backend.CoreCLR;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest constructs this internal fixture through reflected discovery under DiscoverInternals.")]
internal sealed class WarpCoreCLRAuthenticatedFrameTests
{
    private const ulong Sequence = 0x0102030405060708UL;

    [TestMethod]
    public async Task ActualAuthenticatedReadPreservesEveryRawWordAndReturnsIndependentPayloadAsync()
    {
        uint[] words = [0u, 1u, 0x80000000u, 0x7FC00001u, 0xFFC01234u, 0x7F800000u, 0xFF800000u, 0xFFFFFFFFu, 0x00000001u, 0x7FEFFFFFu];
        byte[] payload = new byte[words.Length * sizeof(uint)]; byte[] key = Key();
        for (int index = 0; index < words.Length; index++) { BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(index * sizeof(uint)), words[index]); }
        WarpCoreCLRWorkerProtocol.Frame frame = await RoundTripAsync(key, WarpCoreCLRWorkerProtocol.Executed, payload).ConfigureAwait(false);
        byte[] authenticated = WarpCoreCLRWorkerProtocol.RequireAuthenticatedFrame(frame, key, WarpCoreCLRWorkerProtocol.Executed, Sequence);
        Assert.AreNotSame(frame.Payload, authenticated); CollectionAssert.AreEqual(payload, authenticated);
        uint[] observed = new uint[words.Length];
        for (int index = 0; index < observed.Length; index++) { observed[index] = BinaryPrimitives.ReadUInt32LittleEndian(authenticated.AsSpan(index * sizeof(uint))); }
        CollectionAssert.AreEqual(words, observed);
        authenticated[0] ^= 1;
        CollectionAssert.AreEqual(payload, WarpCoreCLRWorkerProtocol.RequireAuthenticatedFrame(frame, key, WarpCoreCLRWorkerProtocol.Executed, Sequence));
    }

    [TestMethod]
    public async Task ForgedConstructorsAndRecordClonesHaveNoReadReceiptAsync()
    {
        byte[] key = Key(); WarpCoreCLRWorkerProtocol.Frame frame = await RoundTripAsync(key, WarpCoreCLRWorkerProtocol.Hello, [0x80, 0, 0xFF]).ConfigureAwait(false);
        var forged = new WarpCoreCLRWorkerProtocol.Frame(frame.Kind, frame.Payload); WarpCoreCLRWorkerProtocol.Frame clone = frame with { };
        Assert.ThrowsExactly<InvalidDataException>(() => WarpCoreCLRWorkerProtocol.RequireAuthenticatedFrame(forged, key, frame.Kind, Sequence));
        Assert.ThrowsExactly<InvalidDataException>(() => WarpCoreCLRWorkerProtocol.RequireAuthenticatedFrame(clone, key, frame.Kind, Sequence));
        CollectionAssert.AreEqual(frame.Payload, WarpCoreCLRWorkerProtocol.RequireAuthenticatedFrame(frame, key, frame.Kind, Sequence));
    }

    [TestMethod]
    public async Task EqualBytesInAnotherKeyArrayWrongKindAndWrongSequenceAreRejectedAsync()
    {
        byte[] key = Key(); WarpCoreCLRWorkerProtocol.Frame frame = await RoundTripAsync(key, WarpCoreCLRWorkerProtocol.Compiled, [1, 2, 3, 4]).ConfigureAwait(false);
        Assert.ThrowsExactly<InvalidDataException>(() => WarpCoreCLRWorkerProtocol.RequireAuthenticatedFrame(frame, (byte[])key.Clone(), frame.Kind, Sequence));
        Assert.ThrowsExactly<InvalidDataException>(() => WarpCoreCLRWorkerProtocol.RequireAuthenticatedFrame(frame, key, WarpCoreCLRWorkerProtocol.Executed, Sequence));
        Assert.ThrowsExactly<InvalidDataException>(() => WarpCoreCLRWorkerProtocol.RequireAuthenticatedFrame(frame, key, frame.Kind, Sequence + 1));
    }

    [TestMethod]
    public async Task PayloadAndSessionKeyByteMutationAreRejectedAsync()
    {
        byte[] key = Key(); WarpCoreCLRWorkerProtocol.Frame frame = await RoundTripAsync(key, WarpCoreCLRWorkerProtocol.Executed, [1, 2, 3, 4]).ConfigureAwait(false);
        frame.Payload[^1] ^= 0x80;
        Assert.ThrowsExactly<InvalidDataException>(() => WarpCoreCLRWorkerProtocol.RequireAuthenticatedFrame(frame, key, frame.Kind, Sequence));
        frame = await RoundTripAsync(key, WarpCoreCLRWorkerProtocol.Executed, [1, 2, 3, 4]).ConfigureAwait(false);
        key[0] ^= 0x80;
        Assert.ThrowsExactly<InvalidDataException>(() => WarpCoreCLRWorkerProtocol.RequireAuthenticatedFrame(frame, key, frame.Kind, Sequence));
    }

    [TestMethod]
    public async Task ExistingWireGuardsRejectChangedSequenceAndPayloadBeforeReceiptIssuanceAsync()
    {
        byte[] key = Key(); using var stream = new MemoryStream();
        await WarpCoreCLRWorkerProtocol.WriteAsync(stream, key, WarpCoreCLRWorkerProtocol.Hello, Sequence, [1, 2, 3, 4], CancellationToken.None).ConfigureAwait(false);
        stream.Position = 0;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => WarpCoreCLRWorkerProtocol.ReadAsync(stream, key, Sequence + 1, CancellationToken.None)).ConfigureAwait(false);
        byte[] wire = stream.ToArray(); wire[^1] ^= 0x80; using var altered = new MemoryStream(wire);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => WarpCoreCLRWorkerProtocol.ReadAsync(altered, key, Sequence, CancellationToken.None)).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task EmptyPayloadAndOrdinaryUnknownKindRetainTheirExistingWireBehaviorAsync()
    {
        byte[] key = Key(); const ushort ordinaryKind = 0xFFEE;
        WarpCoreCLRWorkerProtocol.Frame frame = await RoundTripAsync(key, ordinaryKind, []).ConfigureAwait(false);
        Assert.AreEqual(ordinaryKind, frame.Kind); Assert.IsEmpty(WarpCoreCLRWorkerProtocol.RequireAuthenticatedFrame(frame, key, ordinaryKind, Sequence));
    }

    private static async Task<WarpCoreCLRWorkerProtocol.Frame> RoundTripAsync(byte[] key, ushort kind, byte[] payload)
    {
        using var stream = new MemoryStream();
        await WarpCoreCLRWorkerProtocol.WriteAsync(stream, key, kind, Sequence, payload, CancellationToken.None).ConfigureAwait(false);
        stream.Position = 0;
        return await WarpCoreCLRWorkerProtocol.ReadAsync(stream, key, Sequence, CancellationToken.None).ConfigureAwait(false);
    }

    private static byte[] Key() => [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31];
}
