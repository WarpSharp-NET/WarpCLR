using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

// This separate end-only contract does not relax the original retained-origin
// checkpoint. A runtime still needs private authenticated dispatch/publication.
internal static class WarpSourceSegmentGuardedEndContract
{
    internal const string Version = "warp.source-guarded-end/exact-dispatch-target-original-ancestors-alias-retirement-full-bank-consistency-only/0.1";

    internal static WarpSourceSegmentGuardedDisposition ValidateCandidate(WarpPortableSourceSegmentMap map,
        WarpPortableSourceSegment segment, WarpPortableSourceGuardedFrontier frontier,
        WarpSourceSegmentSnapshot start, WarpSourceSegmentSnapshot end, int maximumDepth,
        string expectedStateHash, string expectedArenaHash)
    {
        map.RequireExactGuardedFrontier(segment, frontier);
        WarpSourceSegmentBankContract.Validate(map.Layout, start.State.AsSpan(), maximumDepth);
        int sourceDepth = checked((int)start.State[WarpLogicalMachineLayout.DepthOffset]);
        _ = new WarpSourceSegmentCheckpointContract(map, segment, start, maximumDepth, sourceDepth);
        WarpSourceSegmentBankContract.Validate(map.Layout, end.State.AsSpan(), maximumDepth);
        ValidateIdentity(map, start, end, expectedStateHash, expectedArenaHash);
        int endDepth = checked((int)end.State[WarpLogicalMachineLayout.DepthOffset]);
        int target = checked(WarpLogicalMachineLayout.HeaderWords + (endDepth - 1) * map.Layout.FrameWords);
        if (end.State[WarpLogicalMachineLayout.StatusOffset] != WarpLogicalMachineLayout.Runnable ||
            end.State[WarpLogicalMachineLayout.SourceBoundaryStateOffset] != WarpLogicalMachineLayout.BeforeSourceBoundary ||
            end.State[target] != frontier.EndFunction || end.State[target + WarpLogicalMachineLayout.FrameProgramCounterOffset] != frontier.EndProgramCounter)
        { throw new InvalidOperationException("Only the declared guarded first-guest end is a candidate; an intermediate replacement is denied."); }
        ValidateAncestors(map.Layout, start.State.AsSpan(), end.State.AsSpan(), Math.Min(sourceDepth - 1, endDepth));
        if (endDepth < sourceDepth) { return WarpSourceSegmentGuardedDisposition.OriginUnwound; }
        int origin = checked(WarpLogicalMachineLayout.HeaderWords + (sourceDepth - 1) * map.Layout.FrameWords);
        if (end.State[origin] == start.State[origin] &&
            end.State[origin + WarpLogicalMachineLayout.FrameActivationOffset] == start.State[origin + WarpLogicalMachineLayout.FrameActivationOffset])
        { return WarpSourceSegmentGuardedDisposition.OriginRetained; }
        return ValidateReplacement(map.Layout, frontier, start.State.AsSpan(), end.State.AsSpan(), sourceDepth, endDepth);
    }

    private static void ValidateIdentity(WarpPortableSourceSegmentMap map, WarpSourceSegmentSnapshot start,
        WarpSourceSegmentSnapshot end, string expectedStateHash, string expectedArenaHash)
    {
        ulong before = WarpSourceSegmentCheckpointContract.Remaining(start.State.AsSpan());
        if (before == 0 || WarpSourceSegmentCheckpointContract.Remaining(end.State.AsSpan()) != before - 1 ||
            start.ProcessId != end.ProcessId || start.Module != end.Module || end.Ordinal <= start.Ordinal || end.Sequence <= start.Sequence ||
            !string.Equals(end.IrHash, map.IrHash, StringComparison.Ordinal) || !string.Equals(end.StateHash, expectedStateHash, StringComparison.Ordinal) ||
            !string.Equals(end.ArenaHash, expectedArenaHash, StringComparison.Ordinal) ||
            end.State[WarpLogicalMachineLayout.OwnerContextOffset] != start.State[WarpLogicalMachineLayout.OwnerContextOffset] ||
            end.State[WarpLogicalMachineLayout.NextActivationOffset] < start.State[WarpLogicalMachineLayout.NextActivationOffset])
        { throw new InvalidOperationException("The guarded full-bank end differs from its exact one-charge identity."); }
    }

    private static void ValidateAncestors(WarpLogicalMachineLayout layout, ReadOnlySpan<uint> start,
        ReadOnlySpan<uint> end, int surviving)
    {
        for (int depth = 0; depth < surviving; depth++)
        {
            int frame = checked(WarpLogicalMachineLayout.HeaderWords + depth * layout.FrameWords);
            for (int word = 0; word < WarpLogicalMachineLayout.FrameHeaderWords; word++)
            {
                if (word == WarpLogicalMachineLayout.FrameProgramCounterOffset) { continue; }
                if (start[frame + word] != end[frame + word])
                { throw new InvalidOperationException("Guarded dispatch cannot replace an original surviving ancestor or its tuple/alias provenance."); }
            }
        }
    }

    private static WarpSourceSegmentGuardedDisposition ValidateReplacement(WarpLogicalMachineLayout layout,
        WarpPortableSourceGuardedFrontier frontier, ReadOnlySpan<uint> start, ReadOnlySpan<uint> end, int originDepth, int endDepth)
    {
        int frame = checked(WarpLogicalMachineLayout.HeaderWords + (endDepth - 1) * layout.FrameWords);
        if (endDepth != originDepth || frontier.AliasOwnerFunction < 0 ||
            end[frame + WarpLogicalMachineLayout.FrameActivationOffset] <= start[WarpLogicalMachineLayout.NextActivationOffset] ||
            end[frame + WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset] >= originDepth ||
            layout.GetAliasPrefixWords(frontier.EndFunction) != frontier.AliasPrefixWords)
        { throw new InvalidOperationException("A replaced origin needs its exact fresh bound filter alias and surviving prefix owner."); }
        return WarpSourceSegmentGuardedDisposition.OriginReplacedByBoundAlias;
    }
}
