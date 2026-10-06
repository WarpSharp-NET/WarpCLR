using System.Collections.Immutable;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

// Correct ordering/words are necessary, but these caller-visible consistency
// records are NEVER the opaque authenticated generated-publication receipts.
internal sealed class WarpSourceSegmentPublication
{
    private readonly ImmutableArray<uint> expectedRoots;
    private readonly uint beforeRevision;

    internal WarpSourceSegmentPublication(ReadOnlySpan<uint> expectedRoots, uint beforeRevision)
    {
        if (expectedRoots.Length % 3 != 0 || expectedRoots.Length > WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry || beforeRevision == uint.MaxValue)
        { throw new ArgumentException("Precise roots require complete triples and a nonwrapping next revision.", nameof(expectedRoots)); }
        for (int word = 0; word < expectedRoots.Length; word += 3)
        {
            int nonzero = (expectedRoots[word] != 0 ? 1 : 0) + (expectedRoots[word + 1] != 0 ? 1 : 0) + (expectedRoots[word + 2] != 0 ? 1 : 0);
            if (nonzero is not (0 or 3)) { throw new ArgumentException("A precise reference is null or a complete context/object/generation triple.", nameof(expectedRoots)); }
        }
        this.expectedRoots = ImmutableArray.Create(expectedRoots.ToArray()); this.beforeRevision = beforeRevision;
    }

    internal void ValidateCandidate(ReadOnlySpan<uint> publishedRoots, IEnumerable<WarpSourcePublicationStep> steps,
        ulong segmentStartOrdinal, ulong endOrdinal, string committedArenaHash)
    {
        WarpSourcePublicationStep[] rows = steps.Take(5).ToArray();
        WarpSourcePublicationKind[] order = [WarpSourcePublicationKind.CallerTupleStored, WarpSourcePublicationKind.RootsPublished,
            WarpSourcePublicationKind.ResultAcknowledged, WarpSourcePublicationKind.LeaseReleased];
        if (!publishedRoots.SequenceEqual(expectedRoots.AsSpan()) || rows.Length != order.Length ||
            !rows.Select(row => row.Kind).SequenceEqual(order) ||
            rows.Any(row => row.CommandOrdinal <= segmentStartOrdinal || row.CommandOrdinal > endOrdinal ||
                row.ArenaHash.Length != 64 || row.ArenaHash.Any(character => !char.IsAsciiHexDigit(character))) ||
            rows.Skip(1).Any(row => row.RootRevision != beforeRevision + 1) ||
            rows[0].RootRevision != beforeRevision && rows[0].RootRevision != beforeRevision + 1 ||
            rows.Zip(rows.Skip(1)).Any(pair => pair.First.CommandOrdinal > pair.Second.CommandOrdinal) ||
            rows[^1].CommandOrdinal != endOrdinal ||
            !string.Equals(rows[^1].ArenaHash, committedArenaHash, StringComparison.Ordinal))
        { throw new InvalidOperationException("Publication must finish the tuple, exact precise roots, result acknowledgement and lease before release."); }
    }
}
