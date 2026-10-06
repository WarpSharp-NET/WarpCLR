using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this fixture.")]
internal sealed class WarpSourceEventCheckpointTests
{
    [TestMethod]
    [DataRow("namespace")]
    [DataRow("activation")]
    [DataRow("function")]
    [DataRow("pc")]
    [DataRow("depth")]
    [DataRow("private-width")]
    [DataRow("stride")]
    [DataRow("source-depth")]
    [DataRow("extra-charge")]
    [DataRow("budget-increase")]
    [DataRow("activation-wrap")]
    public void ForgedOrIncompleteLiveBanksCannotMatchACommittedCheckpoint(string corruption)
    {
        WarpSourceEventFixture fixture = WarpSourceEventFixture.Capture();
        WarpSourceSegmentSnapshot origin = fixture.CandidateOrigin();
        var contract = new WarpSourceSegmentCheckpointContract(fixture.Map, fixture.Map.Segments[0], origin, WarpSourceEventFixture.MaximumDepth, 2);
        WarpSourceSegmentSnapshot bad = WarpSourceEventFixture.Copy(origin, state => Corrupt(state, corruption, WarpLogicalMachineLayout.HeaderWords + fixture.Map.Layout.FrameWords));
        Assert.ThrowsExactly<InvalidOperationException>(() => contract.ValidateExactCommittedCandidate(bad, origin, 2, 2, bad.StateHash, bad.ArenaHash));
    }

    [TestMethod]
    public void MatchingBoundaryWordsDoNotOverridePrivateSsaOrArenaCheckpointDigests()
    {
        WarpSourceEventFixture fixture = WarpSourceEventFixture.Capture();
        WarpSourceSegmentSnapshot origin = fixture.CandidateOrigin();
        var contract = new WarpSourceSegmentCheckpointContract(fixture.Map, fixture.Map.Segments[0], origin, WarpSourceEventFixture.MaximumDepth, 2);
        WarpSourceSegmentSnapshot expected = WarpSourceEventFixture.Copy(origin);
        WarpSourceSegmentSnapshot bad = WarpSourceEventFixture.Copy(origin, state => state[WarpLogicalMachineLayout.HeaderWords + fixture.Map.Layout.FrameWords + fixture.Map.Layout.PrivateOffset] ^= 1);
        Assert.ThrowsExactly<InvalidOperationException>(() => contract.ValidateExactCommittedCandidate(bad, origin, 2, 2, expected.StateHash, expected.ArenaHash));
        var wrongArena = new WarpSourceSegmentSnapshot(2, 2, origin.ProcessId, origin.Module, origin.IrHash, expected.State.AsSpan(), [0, 0]);
        Assert.ThrowsExactly<InvalidOperationException>(() => contract.ValidateExactCommittedCandidate(wrongArena, origin, 2, 2, expected.StateHash, expected.ArenaHash));
    }

    [TestMethod]
    public void ReplayedSkippedWrongModuleOrWrongProcessCheckpointsAreRejected()
    {
        WarpSourceEventFixture fixture = WarpSourceEventFixture.Capture(); WarpSourceSegmentSnapshot origin = fixture.CandidateOrigin();
        var contract = new WarpSourceSegmentCheckpointContract(fixture.Map, fixture.Map.Segments[0], origin, WarpSourceEventFixture.MaximumDepth, 2);
        WarpSourceSegmentSnapshot[] bad = [WarpSourceEventFixture.Copy(origin, ordinal: 1), WarpSourceEventFixture.Copy(origin, sequence: 3),
            WarpSourceEventFixture.Copy(origin, module: Guid.NewGuid()), WarpSourceEventFixture.Copy(origin, process: 124),
            WarpSourceEventFixture.Copy(origin, irHash: new string('0', 64))];
        foreach (WarpSourceSegmentSnapshot candidate in bad)
        { Assert.ThrowsExactly<InvalidOperationException>(() => contract.ValidateExactCommittedCandidate(candidate, origin, 2, 2, candidate.StateHash, candidate.ArenaHash)); }
    }

    [TestMethod]
    public void TruncatedOriginAndCopiedSegmentCannotStartEvenAConsistencyContract()
    {
        WarpSourceEventFixture fixture = WarpSourceEventFixture.Capture(); WarpSourceSegmentSnapshot origin = fixture.CandidateOrigin();
        var shortBank = new WarpSourceSegmentSnapshot(1, 1, origin.ProcessId, origin.Module, origin.IrHash, [0], []);
        Assert.ThrowsExactly<InvalidOperationException>(() => new WarpSourceSegmentCheckpointContract(fixture.Map, fixture.Map.Segments[0], shortBank, WarpSourceEventFixture.MaximumDepth, 2));
        Assert.ThrowsExactly<InvalidOperationException>(() => new WarpSourceSegmentCheckpointContract(fixture.Map, fixture.Map.Segments[0] with { }, origin, WarpSourceEventFixture.MaximumDepth, 2));
    }

    [TestMethod]
    public void SnapshotCopiesBanksAndHashesCanonicalLittleEndianBytes()
    {
        uint[] words = [0x01020304, 0x80000000, uint.MaxValue]; byte[] bytes = new byte[words.Length * 4];
        for (int index = 0; index < words.Length; index++) { BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * 4), words[index]); }
        var snapshot = new WarpSourceSegmentSnapshot(1, 1, 123, WarpSourceEventFixture.CandidateModule, new string('A', 64), words, []);
        words[0] = 0;
        Assert.AreEqual(0x01020304u, snapshot.State[0]); Assert.AreEqual(Convert.ToHexString(SHA256.HashData(bytes)), snapshot.StateHash, StringComparer.Ordinal);
    }

    private static void Corrupt(uint[] state, string corruption, int frame)
    {
        switch (corruption)
        {
            case "namespace": state[WarpLogicalMachineLayout.OwnerContextOffset]++; break;
            case "activation": state[frame + WarpLogicalMachineLayout.FrameActivationOffset] = 0; break;
            case "function": state[frame + WarpLogicalMachineLayout.FrameFunctionOffset] = 0; break;
            case "pc": state[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset] = uint.MaxValue; break;
            case "depth": state[WarpLogicalMachineLayout.DepthOffset] = uint.MaxValue; break;
            case "private-width": state[frame + WarpLogicalMachineLayout.FramePrivateWordsOffset]++; break;
            case "stride": state[WarpLogicalMachineLayout.FrameStrideOffset]++; break;
            case "source-depth": state[WarpLogicalMachineLayout.LogicalDepthOffset]++; break;
            case "extra-charge": state[WarpLogicalMachineLayout.RemainingStepsLowOffset] -= 2; break;
            case "budget-increase": state[WarpLogicalMachineLayout.RemainingStepsLowOffset]++; break;
            case "activation-wrap": state[WarpLogicalMachineLayout.NextActivationOffset] = 0; break;
            default: throw new ArgumentOutOfRangeException(nameof(corruption));
        }
    }
}
