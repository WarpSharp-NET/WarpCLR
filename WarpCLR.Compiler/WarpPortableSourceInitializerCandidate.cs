using System.Collections.Immutable;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

// Structural reachability is a candidate for a private runtime check, never its receipt.
internal sealed class WarpPortableSourceInitializerCandidate
{
    internal WarpPortableSourceInitializerCandidate(string projectionHash, int entryProgramCounter,
        ImmutableArray<int> programCounters, ImmutableArray<int> helperFunctions,
        ImmutableArray<WarpPortableSourceInitializerFrontier> frontiers, int examinedStates)
    {
        ProjectionHash = projectionHash; EntryProgramCounter = entryProgramCounter;
        ProgramCounters = programCounters; HelperFunctions = helperFunctions; Frontiers = frontiers; ExaminedStates = examinedStates;
        CandidateHash = WarpPortableSnapshotIdentity.Hash(new
        {
            WarpPortableSourceInitializerExecutableProjection.Semantics, ProjectionHash, EntryProgramCounter,
            ProgramCounters, HelperFunctions, Frontiers, ExaminedStates,
            Admission = "structural-candidate-only-no-issuer-no-end-receipt-no-source-event-or-factory-grant",
        });
    }

    internal string ProjectionHash { get; }
    internal string CandidateHash { get; }
    internal int EntryProgramCounter { get; }
    internal ImmutableArray<int> ProgramCounters { get; }
    internal ImmutableArray<int> HelperFunctions { get; }
    internal ImmutableArray<WarpPortableSourceInitializerFrontier> Frontiers { get; }
    internal int ExaminedStates { get; }
    internal bool RequiresRuntimeReceipt { get; } = true;
}
