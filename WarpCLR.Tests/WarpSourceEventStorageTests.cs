using System.Diagnostics.CodeAnalysis;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this fixture.")]
internal sealed class WarpSourceEventStorageTests
{
    [TestMethod]
    [DataRow("arena")]
    [DataRow("state")]
    [DataRow("input")]
    [DataRow("scalar")]
    public void HiddenStorageAliasesStillParticipateForEveryOrdinaryRoute(string location)
    {
        uint[] arena = [1, 2]; uint[] state = [3, 4]; var contract = new WarpSourceArenaStorageContract(arena, [state]);
        uint[] requestArena = string.Equals(location, "arena", StringComparison.Ordinal) ? arena : [8];
        uint[][] requestStates = string.Equals(location, "state", StringComparison.Ordinal) ? [state] : [[9]];
        uint[][] inputs = string.Equals(location, "input", StringComparison.Ordinal) ? [arena] : [[10]];
        uint[] scalars = string.Equals(location, "scalar", StringComparison.Ordinal) ? state : [11];
        foreach (WarpSourceArenaRoute route in Enum.GetValues<WarpSourceArenaRoute>().Where(route => route <= WarpSourceArenaRoute.NativeExecution))
        { Assert.AreEqual(WarpSourceArenaDecision.Excluded, contract.ClassifyCandidate(WarpSourceArenaPhase.SegmentHeld, route, requestStates, requestArena, inputs, scalars), route.ToString()); }
    }

    [TestMethod]
    public void OmittedWrongStorageOrForgedPrivatePurposeCannotSupplyAnEventPermit()
    {
        uint[] arena = [1]; uint[] state = [2]; var contract = new WarpSourceArenaStorageContract(arena, [state]);
        Assert.AreEqual(WarpSourceArenaDecision.Excluded, contract.ClassifyCandidate(WarpSourceArenaPhase.SegmentHeld, WarpSourceArenaRoute.OwnedSource, [], arena, [], []));
        Assert.AreEqual(WarpSourceArenaDecision.Excluded, contract.ClassifyCandidate(WarpSourceArenaPhase.SegmentHeld, WarpSourceArenaRoute.OwnedSource, [[2]], arena, [], []));
        Assert.AreEqual(WarpSourceArenaDecision.Excluded, contract.ClassifyCandidate(WarpSourceArenaPhase.SegmentHeld, WarpSourceArenaRoute.OwnedSource, [state], [1], [], []));
        Assert.AreEqual(WarpSourceArenaDecision.OpaqueSegmentReceiptRequired, contract.ClassifyCandidate(WarpSourceArenaPhase.SegmentHeld, WarpSourceArenaRoute.OwnedSource, [state], arena, [], []));
    }

    [TestMethod]
    [DataRow(33)]
    [DataRow(131)]
    public void FullStorageCensusIsImmutableAndHasNoPreparedKindParticipantLimit(int participants)
    {
        uint[] arena = [0]; uint[][] states = Enumerable.Range(0, participants).Select(index => new[] { (uint)index }).ToArray();
        uint[] last = states[^1]; var contract = new WarpSourceArenaStorageContract(arena, states); states[^1] = [999];
        Assert.AreEqual(WarpSourceArenaDecision.Excluded, contract.ClassifyCandidate(WarpSourceArenaPhase.SegmentHeld, WarpSourceArenaRoute.BatchState, [last], [], [], []));
        Assert.AreEqual(WarpSourceArenaDecision.ExistingAdmissionRequired, contract.ClassifyCandidate(WarpSourceArenaPhase.SegmentHeld, WarpSourceArenaRoute.Read8, [[999]], [0], [], []));
        Assert.AreEqual(WarpSourceArenaDecision.Excluded, contract.ClassifyCandidate(WarpSourceArenaPhase.TerminalQuarantine, WarpSourceArenaRoute.FixedStoppedCleanup, [last], arena, [], []));
    }

    [TestMethod]
    public void DuplicatedNullOrArenaAliasingCensusBanksAreRejected()
    {
        uint[] arena = [1]; uint[] state = [2];
        Assert.ThrowsExactly<ArgumentException>(() => new WarpSourceArenaStorageContract(arena, []));
        Assert.ThrowsExactly<ArgumentException>(() => new WarpSourceArenaStorageContract(arena, [state, state]));
        Assert.ThrowsExactly<ArgumentException>(() => new WarpSourceArenaStorageContract(arena, [null!]));
        Assert.ThrowsExactly<ArgumentException>(() => new WarpSourceArenaStorageContract(arena, [arena]));
    }
}
