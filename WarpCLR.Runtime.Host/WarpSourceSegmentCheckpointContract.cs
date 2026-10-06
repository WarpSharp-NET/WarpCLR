using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal sealed class WarpSourceSegmentCheckpointContract
{
    internal const string Version = "warp.source-checkpoint/full-banks-exact-module-origin-one-source-charge-consistency-only/0.1";
    private readonly WarpPortableSourceSegmentMap map;
    private readonly WarpPortableSourceSegment segment;
    private readonly WarpSourceSegmentSnapshot previous;
    private readonly int maximumDepth, sourceDepth, sourceFrame;
    private readonly uint ownerContext, activation;
    private readonly ulong remaining;

    internal WarpSourceSegmentCheckpointContract(WarpPortableSourceSegmentMap map, WarpPortableSourceSegment segment,
        WarpSourceSegmentSnapshot previous, int maximumDepth, int sourceDepth)
    {
        this.map = map; this.segment = segment; this.previous = previous; this.maximumDepth = maximumDepth; this.sourceDepth = sourceDepth;
        map.RequireExactSegment(segment);
        ValidateRuntimeState(previous.State.AsSpan());
        if (
            !string.Equals(previous.IrHash, map.IrHash, StringComparison.Ordinal) || !map.Layout.HasFrameOwners ||
            previous.State[WarpLogicalMachineLayout.StatusOffset] != WarpLogicalMachineLayout.Runnable ||
            sourceDepth <= 0 || (uint)sourceDepth != previous.State[WarpLogicalMachineLayout.DepthOffset])
        { throw new InvalidOperationException("The origin must be an exact compiler segment and live owned source frame."); }
        sourceFrame = checked(WarpLogicalMachineLayout.HeaderWords + (sourceDepth - 1) * map.Layout.FrameWords);
        if (previous.State[sourceFrame + WarpLogicalMachineLayout.FrameFunctionOffset] != segment.Function ||
            previous.State[sourceFrame + WarpLogicalMachineLayout.FrameProgramCounterOffset] != segment.EntryProgramCounter ||
            previous.State[WarpLogicalMachineLayout.SourceBoundaryModeOffset] != 1 ||
            previous.State[WarpLogicalMachineLayout.SourceBoundaryStateOffset] != WarpLogicalMachineLayout.BeforeSourceBoundary)
        { throw new InvalidOperationException("The origin is not parked before the exact original source instruction."); }
        ownerContext = previous.State[WarpLogicalMachineLayout.OwnerContextOffset];
        activation = previous.State[sourceFrame + WarpLogicalMachineLayout.FrameActivationOffset];
        remaining = Remaining(previous.State.AsSpan());
        if (ownerContext == 0 || activation == 0 || remaining == 0) { throw new InvalidOperationException("The live source namespace, activation and budget cannot be zero."); }
    }

    internal void ValidateExactCommittedCandidate(WarpSourceSegmentSnapshot candidate, WarpSourceSegmentSnapshot preceding,
        ulong expectedOrdinal, ulong expectedSequence,
        string expectedStateHash, string expectedArenaHash)
    {
        if (preceding.Ordinal < previous.Ordinal || preceding.ProcessId != previous.ProcessId || preceding.Module != previous.Module ||
            !string.Equals(preceding.IrHash, map.IrHash, StringComparison.Ordinal) ||
            candidate.Ordinal <= preceding.Ordinal || candidate.Ordinal != expectedOrdinal || candidate.Sequence != expectedSequence ||
            preceding.Sequence == ulong.MaxValue || candidate.Sequence != preceding.Sequence + 1 || candidate.ProcessId != previous.ProcessId ||
            candidate.Module != previous.Module || !string.Equals(candidate.IrHash, map.IrHash, StringComparison.Ordinal) ||
            !string.Equals(candidate.StateHash, expectedStateHash, StringComparison.Ordinal) ||
            !string.Equals(candidate.ArenaHash, expectedArenaHash, StringComparison.Ordinal))
        { throw new InvalidOperationException("The candidate differs from the exact committed full-bank command checkpoint."); }
        ValidateRuntimeState(preceding.State.AsSpan());
        ValidateRuntimeState(candidate.State.AsSpan());
        ulong now = Remaining(candidate.State.AsSpan());
        if (now > Remaining(preceding.State.AsSpan()) || now > remaining || remaining - now > 1 ||
            candidate.State[WarpLogicalMachineLayout.OwnerContextOffset] != ownerContext || preceding.State[WarpLogicalMachineLayout.OwnerContextOffset] != ownerContext ||
            candidate.State[WarpLogicalMachineLayout.NextActivationOffset] < preceding.State[WarpLogicalMachineLayout.NextActivationOffset] ||
            Used(candidate.State.AsSpan()) < Used(preceding.State.AsSpan()))
        { throw new InvalidOperationException("A held segment cannot change namespaces, wrap activations, or charge another guest instruction."); }
        if ((uint)sourceDepth <= candidate.State[WarpLogicalMachineLayout.DepthOffset] &&
            (candidate.State[sourceFrame + WarpLogicalMachineLayout.FrameFunctionOffset] != segment.Function ||
             candidate.State[sourceFrame + WarpLogicalMachineLayout.FrameActivationOffset] != activation))
        { throw new InvalidOperationException("The candidate replaced its exact retained original source activation."); }
    }

    private void ValidateRuntimeState(ReadOnlySpan<uint> state)
        => WarpSourceSegmentBankContract.Validate(map.Layout, state, maximumDepth);

    internal static ulong Remaining(ReadOnlySpan<uint> state) =>
        state[WarpLogicalMachineLayout.RemainingStepsLowOffset] | (ulong)state[WarpLogicalMachineLayout.RemainingStepsHighOffset] << 32;

    private static ulong Used(ReadOnlySpan<uint> state) =>
        state[WarpLogicalMachineLayout.UsedOperationsLowOffset] | (ulong)state[WarpLogicalMachineLayout.UsedOperationsHighOffset] << 32;
}
