using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal static class WarpSourceSegmentEndContract
{
    internal const string Version = "warp.source-segment-end/exact-frontier-before-guest-publication-no-release-authority/0.1";

    internal static WarpSourceSegmentCandidateResult ValidateCandidate(WarpPortableSourceSegmentMap map,
        WarpPortableSourceSegment segment, WarpSourceSegmentSnapshot end, int maximumDepth, int activeFrame,
        WarpPortableSourceSegmentEndKind kind, ulong remainingBefore)
    {
        ReadOnlySpan<uint> state = end.State.AsSpan();
        map.RequireExactSegment(segment);
        if (!string.Equals(end.IrHash, map.IrHash, StringComparison.Ordinal) || !Enum.IsDefined(kind) ||
            !segment.Frontiers.Any(frontier => frontier.Kind == kind))
        { throw new InvalidOperationException("An end candidate requires the exact compiler-bound IR and frontier class."); }
        WarpSourceSegmentBankContract.Validate(map.Layout, state, maximumDepth);
        if (remainingBefore == 0 || WarpSourceSegmentCheckpointContract.Remaining(state) != remainingBefore - 1)
        { throw new InvalidOperationException("Exactly one original source instruction must finish before an end candidate."); }
        if (kind == WarpPortableSourceSegmentEndKind.SourceReturn && state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Completed)
        {
            // Terminal completion remains consistency data. The opaque runtime
            // must join the exact returned tuple and generated root receipts.
        }
        else if (kind == WarpPortableSourceSegmentEndKind.ManagedExceptionTerminal)
        {
            if (state[WarpLogicalMachineLayout.StatusOffset] != WarpLogicalMachineLayout.Faulted ||
                state[WarpLogicalMachineLayout.FaultKindOffset] != WarpLogicalMachineLayout.ManagedExceptionFault ||
                state[WarpLogicalMachineLayout.EscapedExceptionContextOffset] == 0 ||
                state[WarpLogicalMachineLayout.EscapedExceptionObjectOffset] == 0 || state[WarpLogicalMachineLayout.EscapedExceptionGenerationOffset] == 0)
            { throw new InvalidOperationException("The managed terminal requires its complete escaped reference and distinct common fault."); }
        }
        else
        {
            if (activeFrame <= 0 || activeFrame > state[WarpLogicalMachineLayout.DepthOffset] ||
                state[WarpLogicalMachineLayout.StatusOffset] != WarpLogicalMachineLayout.Runnable ||
                state[WarpLogicalMachineLayout.SourceBoundaryModeOffset] != 1 ||
                state[WarpLogicalMachineLayout.SourceBoundaryStateOffset] != WarpLogicalMachineLayout.BeforeSourceBoundary)
            { throw new InvalidOperationException("A segment must park before original guest/source execution."); }
            int frame = checked(WarpLogicalMachineLayout.HeaderWords + (activeFrame - 1) * map.Layout.FrameWords);
            int function = checked((int)state[frame + WarpLogicalMachineLayout.FrameFunctionOffset]);
            int pc = checked((int)state[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset]);
            if (!segment.Frontiers.Any(frontier => frontier.Kind == kind && frontier.Function == function && frontier.ProgramCounter == pc))
            { throw new InvalidOperationException("A boundary number outside the compiler frontier is not an end candidate."); }
        }
        return WarpSourceSegmentCandidateResult.OpaqueRegistryEndAndPublicationReceiptsRequired;
    }
}
