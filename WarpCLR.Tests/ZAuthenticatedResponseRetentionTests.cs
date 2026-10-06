using System.Diagnostics.CodeAnalysis;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest constructs this internal fixture through reflected discovery under DiscoverInternals.")]
internal sealed class ZAuthenticatedResponseRetentionTests
{
    private const ulong Sequence = 0x0102030405060708UL;

    [TestMethod]
    public async Task VerifiedResponseCopiesPreserveRawWordsAndCannotChangeRetainedEvidenceAsync()
    {
        byte[] key = Key();
        uint[] state = [0, 1, 0x80000000, 0x7FC00001, 0xFFC01234, uint.MaxValue];
        uint[] arena = [0x7F800000, 0xFF800000, 0x7FEFFFFF, 0x00000001];
        byte[] request = WarpCoreCLRWorkerWords.Request([[17]], [], 0, state, 8, 4096, arena);
        byte[] payload = WarpCoreCLRWorkerWords.Response(request, state, arena);
        WarpCoreCLRWorkerProtocol.Frame frame = await ReadFrameAsync(key, payload).ConfigureAwait(false);
        WarpCoreCLRAuthenticatedResponse retained = WarpCoreCLRAuthenticatedResponse.Capture(frame, key,
            WarpCoreCLRWorkerProtocol.Executed, Sequence, out byte[] first);
        Assert.AreNotSame(frame.Payload, first);
        (uint[] actualState, uint[] actualArena) = WarpCoreCLRWorkerWords.ReadResponse(first, request, state.Length, arena.Length);
        CollectionAssert.AreEqual(state, actualState);
        CollectionAssert.AreEqual(arena, actualArena);
        first[^1] ^= 0x80;
        byte[] second = retained.RequirePayload(key, WarpCoreCLRWorkerProtocol.Executed, Sequence);
        Assert.AreNotSame(first, second);
        Assert.AreNotSame(frame.Payload, second);
        CollectionAssert.AreEqual(payload, second);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ConstructedAndRecordClonedFramesCannotCreateRetainedResponseAsync(bool recordClone)
    {
        byte[] key = Key();
        WarpCoreCLRWorkerProtocol.Frame frame = await ReadFrameAsync(key, [0x80, 0, 0xFF]).ConfigureAwait(false);
        WarpCoreCLRWorkerProtocol.Frame forged = recordClone ? frame with { } : new(frame.Kind, (byte[])frame.Payload.Clone());
        Assert.ThrowsExactly<InvalidDataException>(() => WarpCoreCLRAuthenticatedResponse.Capture(forged, key,
            WarpCoreCLRWorkerProtocol.Executed, Sequence, out _));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public async Task RetainedResponseRejectsChangedSessionKindOrSequenceAsync(int mismatch)
    {
        byte[] key = Key();
        WarpCoreCLRWorkerProtocol.Frame frame = await ReadFrameAsync(key, [1, 2, 3, 4]).ConfigureAwait(false);
        WarpCoreCLRAuthenticatedResponse retained = WarpCoreCLRAuthenticatedResponse.Capture(frame, key,
            WarpCoreCLRWorkerProtocol.Executed, Sequence, out _);
        byte[] candidateKey = mismatch == 0 ? (byte[])key.Clone() : key;
        ushort kind = mismatch == 1 ? WarpCoreCLRWorkerProtocol.Compiled : WarpCoreCLRWorkerProtocol.Executed;
        ulong sequence = mismatch == 2 ? Sequence + 1 : Sequence;
        Assert.ThrowsExactly<InvalidDataException>(() => retained.RequirePayload(candidateKey, kind, sequence));
        CollectionAssert.AreEqual(frame.Payload, retained.RequirePayload(key, WarpCoreCLRWorkerProtocol.Executed, Sequence));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RetainedResponseRevalidatesPayloadAndSessionBytesOnEveryUseAsync(bool changeKey)
    {
        byte[] key = Key();
        WarpCoreCLRWorkerProtocol.Frame frame = await ReadFrameAsync(key, [1, 2, 3, 4]).ConfigureAwait(false);
        WarpCoreCLRAuthenticatedResponse retained = WarpCoreCLRAuthenticatedResponse.Capture(frame, key,
            WarpCoreCLRWorkerProtocol.Executed, Sequence, out byte[] original);
        if (changeKey) { key[^1] ^= 0x80; }
        else { frame.Payload[^1] ^= 0x80; }
        Assert.ThrowsExactly<InvalidDataException>(() => retained.RequirePayload(key, WarpCoreCLRWorkerProtocol.Executed, Sequence));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, original);
    }

    [TestMethod]
    public async Task HolderRetainsActualReadReceiptAfterStreamAndFrameScopeEndAsync()
    {
        byte[] key = Key();
        (WarpCoreCLRAuthenticatedResponse retained, WeakReference<WarpCoreCLRWorkerProtocol.Frame> reference) =
            await RetainReadAsync(key).ConfigureAwait(false);
        GC.Collect();
        Assert.IsTrue(reference.TryGetTarget(out WarpCoreCLRWorkerProtocol.Frame? frame));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, retained.RequirePayload(key, WarpCoreCLRWorkerProtocol.Executed, Sequence));
        Assert.IsNotNull(frame);
        frame.Payload[^1] ^= 1;
        Assert.ThrowsExactly<InvalidDataException>(() => retained.RequirePayload(key, WarpCoreCLRWorkerProtocol.Executed, Sequence));
        GC.KeepAlive(retained);
    }

    [TestMethod]
    public async Task ActualCompiledChildCheckpointKeepsTransportAndCompleteBankIdentityAsync()
    {
        var layout = new WarpLogicalMachineLayout(new WarpIntegerMapVerifier().Verify(new WarpIntegerMapRequest(
            typeof(WarpAuthenticatedWordCheckpointKernels).GetMethod(nameof(WarpAuthenticatedWordCheckpointKernels.Calculate))!, 1)).ControlFlow);
        WarpCoreCLRWorkerKernel kernel = await WarpCoreCLRWorkerKernel.CompileAsync(layout, new(), CancellationToken.None).ConfigureAwait(false);
        await using var ownedKernel = kernel.ConfigureAwait(false);
        WarpCoreCLRWorkerLease lease = kernel.TryAcquireLease()!;
        await using var ownedLease = lease.ConfigureAwait(false);
        uint[] state = layout.CreateInitialState(8, 10000);
        uint[] arena = [0x7FC00001, 0xFFC01234, uint.MaxValue, 0x80000000];
        uint[] originalArena = (uint[])arena.Clone();
        await lease.ExecuteManagedQuantumAsync([[17]], [], 0, state, 8, 4096, arena).ConfigureAwait(false);
        WarpCoreCLRWorkerProcess.WordCheckpoint checkpoint = lease.RequireCommittedWordCheckpoint(state, arena);
        Assert.AreNotEqual(Environment.ProcessId, checkpoint.ProcessId);
        Assert.AreEqual(lease.ProcessId, checkpoint.ProcessId);
        Assert.AreEqual(lease.CompiledModule, checkpoint.Module);
        GC.Collect();
        lease.ValidateCommittedWordCheckpoint(checkpoint, state, arena);
        checkpoint.ValidateReadOnlyIdentity(WarpCoreCLRReadOnlyWordIdentity.FromArguments([[17]], []));
        Assert.AreEqual(52u, state[layout.GetResultWordOffset(0, 8)]);
        CollectionAssert.AreEqual(originalArena, arena);
        arena[^1] ^= 1;
        Assert.ThrowsExactly<InvalidOperationException>(() => lease.ValidateCommittedWordCheckpoint(checkpoint, state, arena));
        arena[^1] ^= 1;
        lease.ValidateCommittedWordCheckpoint(checkpoint, state, arena);
    }

    private static async Task<(WarpCoreCLRAuthenticatedResponse Response, WeakReference<WarpCoreCLRWorkerProtocol.Frame> Frame)> RetainReadAsync(byte[] key)
    {
        WarpCoreCLRWorkerProtocol.Frame frame = await ReadFrameAsync(key, [1, 2, 3, 4]).ConfigureAwait(false);
        WarpCoreCLRAuthenticatedResponse retained = WarpCoreCLRAuthenticatedResponse.Capture(frame, key,
            WarpCoreCLRWorkerProtocol.Executed, Sequence, out _);
        return (retained, new(frame));
    }

    private static async Task<WarpCoreCLRWorkerProtocol.Frame> ReadFrameAsync(byte[] key, byte[] payload)
    {
        using var stream = new MemoryStream();
        await WarpCoreCLRWorkerProtocol.WriteAsync(stream, key, WarpCoreCLRWorkerProtocol.Executed, Sequence, payload, CancellationToken.None).ConfigureAwait(false);
        stream.Position = 0;
        return await WarpCoreCLRWorkerProtocol.ReadAsync(stream, key, Sequence, CancellationToken.None).ConfigureAwait(false);
    }

    private static byte[] Key() => [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31];
}
