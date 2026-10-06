using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal static class WarpSourceInvocationEndContract
{
    internal const string Version = "warp.source-invocation-end/canonical-wrapper-private-arguments-zero-charge-before-guest-consistency-only/0.1";

    internal static WarpSourceSegmentCandidateResult ValidateCandidate(WarpPortableSourceSegmentMap map,
        WarpPortableSourceInvocation invocation, WarpSourceSegmentSnapshot start, WarpSourceSegmentSnapshot end,
        int maximumDepth, ReadOnlySpan<uint> arguments, string expectedStateHash, string expectedArenaHash)
    {
        map.RequireExactInvocation(invocation);
        WarpLogicalMachineLayout layout = map.Layout;
        WarpSourceSegmentBankContract.Validate(layout, start.State.AsSpan(), maximumDepth);
        WarpSourceSegmentBankContract.Validate(layout, end.State.AsSpan(), maximumDepth);
        ValidateStart(layout, invocation, start);
        ValidateIdentity(map, start, end, expectedStateHash, expectedArenaHash);
        if (end.State[WarpLogicalMachineLayout.StatusOffset] != WarpLogicalMachineLayout.Runnable ||
            end.State[WarpLogicalMachineLayout.SourceBoundaryStateOffset] != WarpLogicalMachineLayout.BeforeSourceBoundary ||
            WarpSourceSegmentCheckpointContract.Remaining(end.State.AsSpan()) != WarpSourceSegmentCheckpointContract.Remaining(start.State.AsSpan()))
        { throw new InvalidOperationException("Before-body invocation must stop before the first guest charge, never at a status-only or exhausted quantum."); }
        int frame = checked(WarpLogicalMachineLayout.HeaderWords + ((int)end.State[WarpLogicalMachineLayout.DepthOffset] - 1) * layout.FrameWords);
        if (!invocation.Frontiers.Any(frontier => frontier.Function == end.State[frame] &&
            frontier.ProgramCounter == end.State[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset]))
        { throw new InvalidOperationException("The invocation did not reach its compiler-sealed first guest frontier."); }
        ValidateArguments(layout, invocation, end.State.AsSpan(), arguments);
        return WarpSourceSegmentCandidateResult.OpaqueRegistryEndAndPublicationReceiptsRequired;
    }

    private static void ValidateStart(WarpLogicalMachineLayout layout, WarpPortableSourceInvocation invocation, WarpSourceSegmentSnapshot start)
    {
        if (start.State[WarpLogicalMachineLayout.StatusOffset] != WarpLogicalMachineLayout.Runnable ||
            start.State[WarpLogicalMachineLayout.DepthOffset] != 1 || start.State[WarpLogicalMachineLayout.NextActivationOffset] != 1 ||
            start.State[WarpLogicalMachineLayout.SourceBoundaryModeOffset] != 1 || start.State[WarpLogicalMachineLayout.SourceBoundaryStateOffset] != 0 ||
            start.State[WarpLogicalMachineLayout.HeaderWords + WarpLogicalMachineLayout.FrameProgramCounterOffset] != invocation.EntryProgramCounter ||
            start.State[WarpLogicalMachineLayout.UsedOperationsLowOffset] != 0 || start.State[WarpLogicalMachineLayout.UsedOperationsHighOffset] != 0 ||
            !layout.HasFrameOwners)
        { throw new InvalidOperationException("An entry invocation needs its separately admitted canonical initial wrapper, not an original instruction."); }
    }

    private static void ValidateIdentity(WarpPortableSourceSegmentMap map, WarpSourceSegmentSnapshot start,
        WarpSourceSegmentSnapshot end, string expectedStateHash, string expectedArenaHash)
    {
        if (!string.Equals(start.IrHash, map.IrHash, StringComparison.Ordinal) || !string.Equals(end.IrHash, map.IrHash, StringComparison.Ordinal) ||
            start.ProcessId != end.ProcessId || start.Module != end.Module || end.Ordinal <= start.Ordinal || end.Sequence <= start.Sequence ||
            !string.Equals(end.StateHash, expectedStateHash, StringComparison.Ordinal) || !string.Equals(end.ArenaHash, expectedArenaHash, StringComparison.Ordinal) ||
            start.State[WarpLogicalMachineLayout.OwnerContextOffset] != end.State[WarpLogicalMachineLayout.OwnerContextOffset] ||
            end.State[WarpLogicalMachineLayout.NextActivationOffset] < start.State[WarpLogicalMachineLayout.NextActivationOffset])
        { throw new InvalidOperationException("The full-bank invocation checkpoint differs from its exact candidate identity."); }
    }

    private static void ValidateArguments(WarpLogicalMachineLayout layout, WarpPortableSourceInvocation invocation,
        ReadOnlySpan<uint> state, ReadOnlySpan<uint> arguments)
    {
        if (arguments.Length != invocation.Arguments.Sum(slot => slot.Type.WordCount))
        { throw new InvalidOperationException("The before-body argument tuple has an incomplete declared width."); }
        int frame = WarpLogicalMachineLayout.HeaderWords + layout.FrameWords;
        if (state[WarpLogicalMachineLayout.DepthOffset] < 2 || state[frame] != invocation.Function)
        { throw new InvalidOperationException("The initialized original entry frame is absent from the guest frontier."); }
        int input = 0;
        foreach (WarpPortableWordStorageSlot slot in invocation.Arguments)
        {
            int offset = checked(frame + layout.PrivateOffset + slot.WordOffset);
            if (!state.Slice(offset, slot.Type.WordCount).SequenceEqual(arguments.Slice(input, slot.Type.WordCount)))
            { throw new InvalidOperationException("The compiler-private original arguments changed before body entry."); }
            input += slot.Type.WordCount;
        }
    }
}
