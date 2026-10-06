using System.Diagnostics.CodeAnalysis;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this fixture.")]
internal sealed class WarpSourceEventPublicationTests
{
    private const string ArenaHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [TestMethod]
    public void CompleteTupleRootsAcknowledgementAndLeaseOrderIsOnlyCandidateData()
    {
        uint[] roots = [7, 11, 13, 0, 0, 0]; var contract = new WarpSourceSegmentPublication(roots, 40);
        contract.ValidateCandidate(roots, ValidSteps(), 10, 14, ArenaHash);
        roots[0] = 8;
        Assert.ThrowsExactly<InvalidOperationException>(() => contract.ValidateCandidate(roots, ValidSteps(), 10, 14, ArenaHash));
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("reordered")]
    [DataRow("extra")]
    [DataRow("replayed")]
    [DataRow("wrong-revision")]
    [DataRow("wrong-arena")]
    [DataRow("wrong-end")]
    public void IncompleteForgedOrReorderedPublicationCannotCloseAConsistencyContract(string corruption)
    {
        var contract = new WarpSourceSegmentPublication([7, 11, 13], 40);
        WarpSourcePublicationStep[] rows = ValidSteps();
        switch (corruption)
        {
            case "missing": rows = rows[..3]; break;
            case "reordered": (rows[1], rows[2]) = (rows[2], rows[1]); break;
            case "extra": rows = [.. rows, rows[^1]]; break;
            case "replayed": rows[0] = rows[0] with { CommandOrdinal = 10 }; break;
            case "wrong-revision": rows[1] = rows[1] with { RootRevision = 40 }; break;
            case "wrong-arena": rows[^1] = rows[^1] with { ArenaHash = new string('B', 64) }; break;
            case "wrong-end": rows[^1] = rows[^1] with { CommandOrdinal = 13 }; break;
            default: throw new ArgumentOutOfRangeException(nameof(corruption));
        }
        Assert.ThrowsExactly<InvalidOperationException>(() => contract.ValidateCandidate([7, 11, 13], rows, 10, 14, ArenaHash));
    }

    [TestMethod]
    public void PartialReferenceTriplesAndRevisionExhaustionAreRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new WarpSourceSegmentPublication([7, 11], 40));
        Assert.ThrowsExactly<ArgumentException>(() => new WarpSourceSegmentPublication([7, 0, 13], 40));
        Assert.ThrowsExactly<ArgumentException>(() => new WarpSourceSegmentPublication([], uint.MaxValue));
    }

    private static WarpSourcePublicationStep[] ValidSteps() =>
    [new(WarpSourcePublicationKind.CallerTupleStored, 11, ArenaHash, 40), new(WarpSourcePublicationKind.RootsPublished, 12, ArenaHash, 41),
        new(WarpSourcePublicationKind.ResultAcknowledged, 13, ArenaHash, 41), new(WarpSourcePublicationKind.LeaseReleased, 14, ArenaHash, 41)];
}
