using System.Buffers.Binary;
using System.Security.Cryptography;
using WarpCLR.Backend.CoreCLR;

namespace WarpCLR.Tests;

internal sealed partial class WarpCoreCLRWorkerTests
{
    [TestMethod]
    public async Task AuthenticatedFramesRejectMacSequenceLengthAndTruncation()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        using var stream = new MemoryStream();
        await WarpCoreCLRWorkerProtocol.WriteAsync(stream, key, WarpCoreCLRWorkerProtocol.Execute, 9, [1, 2, 3], CancellationToken.None).ConfigureAwait(false);
        byte[] original = stream.ToArray();
        using var valid = new MemoryStream(original, writable: false);
        WarpCoreCLRWorkerProtocol.Frame frame = await WarpCoreCLRWorkerProtocol.ReadAsync(valid, key, 9, CancellationToken.None).ConfigureAwait(false);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, frame.Payload);
        using var wrongSequence = new MemoryStream(original, writable: false);
        await Assert.ThrowsAsync<InvalidDataException>(() => WarpCoreCLRWorkerProtocol.ReadAsync(wrongSequence, key, 8, CancellationToken.None)).ConfigureAwait(false);
        byte[] corrupt = (byte[])original.Clone(); corrupt[^1] ^= 1;
        using var corruptStream = new MemoryStream(corrupt, writable: false);
        await Assert.ThrowsAsync<InvalidDataException>(() => WarpCoreCLRWorkerProtocol.ReadAsync(corruptStream, key, 9, CancellationToken.None)).ConfigureAwait(false);
        byte[] oversized = (byte[])original.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(oversized.AsSpan(16), WarpCoreCLRWorkerProtocol.MaximumPayloadBytes + 1);
        using var oversizedStream = new MemoryStream(oversized, writable: false);
        await Assert.ThrowsAsync<InvalidDataException>(() => WarpCoreCLRWorkerProtocol.ReadAsync(oversizedStream, key, 9, CancellationToken.None)).ConfigureAwait(false);
        using var truncated = new MemoryStream(original[..^1], writable: false);
        await Assert.ThrowsAsync<EndOfStreamException>(() => WarpCoreCLRWorkerProtocol.ReadAsync(truncated, key, 9, CancellationToken.None)).ConfigureAwait(false);
    }

    [TestMethod]
    public void WordResponseBindsExactInvocationDigestAndBothMutableShapes()
    {
        byte[] request = WarpCoreCLRWorkerWords.Request([[1, 2]], [3], 1, [4, 5], 7, 9, [10]);
        byte[] response = WarpCoreCLRWorkerWords.Response(request, [11, 12], [13]);
        (uint[] state, uint[] arena) = WarpCoreCLRWorkerWords.ReadResponse(response, request, 2, 1);
        CollectionAssert.AreEqual(new uint[] { 11, 12 }, state); CollectionAssert.AreEqual(new uint[] { 13 }, arena);
        Assert.Throws<InvalidDataException>(() => WarpCoreCLRWorkerWords.ReadResponse(response, request, 3, 1));
        byte[] other = (byte[])request.Clone(); other[^1] ^= 1;
        Assert.Throws<InvalidDataException>(() => WarpCoreCLRWorkerWords.ReadResponse(response, other, 2, 1));
        byte[] corrupt = (byte[])request.Clone(); BinaryPrimitives.WriteInt32LittleEndian(corrupt.AsSpan(16), int.MaxValue);
        Assert.Throws<InvalidDataException>(() => WarpCoreCLRWorkerWords.ReadRequest(corrupt));
    }
}
