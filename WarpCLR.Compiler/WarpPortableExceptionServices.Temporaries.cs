using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableExceptionServices
{
    // These checks run before any owner clear. The actual executing state bank
    // stays under the source-operation lease through all helper quanta and the
    // following atomic control transfer. A scalar lease is not private authority.
    private static uint ValidateTemporaryCleanup(uint[] state, uint[] arena, uint descriptor, uint worker, uint index, uint boundary)
    {
        uint record = Record(arena, descriptor, worker, index);
        if (boundary == 0 || arena[record + WarpPortableExceptionLayout.FrameCount] == 0 ||
            arena[record + WarpPortableExceptionLayout.FrameCount] > arena[descriptor + WarpPortableExceptionLayout.MaximumFrames] ||
            arena[record + WarpPortableExceptionLayout.TemporaryOwnersRetired] != 0 ||
            ValidateCapture(state, arena, descriptor, Worker(arena, descriptor, worker)) == 0)
        { return WarpPortableExceptionLayout.InvalidContinuation; }
        for (uint ordinal = 1; ordinal <= arena[record + WarpPortableExceptionLayout.FrameCount]; ordinal++)
        {
            uint captured = Frame(arena, descriptor, worker, index, ordinal);
            uint fault = ValidateTemporaryFrame(state, arena, descriptor, captured);
            if (fault != 0) { return fault; }
        }
        return 0;
    }

    private static uint ValidateTemporaryFrame(uint[] state, uint[] arena, uint descriptor, uint captured)
    {
        uint siteId = arena[captured + WarpPortableExceptionLayout.FrameSite];
        if (siteId == 0 || siteId > arena[descriptor + WarpPortableExceptionLayout.SiteCount]) { return WarpPortableExceptionLayout.InvalidDescriptor; }
        uint site = Site(arena, descriptor, siteId);
        if (arena[site + WarpPortableExceptionLayout.SiteFunction] != arena[captured + WarpPortableExceptionLayout.FrameFunction] ||
            LookupPc(arena, descriptor, arena[captured + WarpPortableExceptionLayout.FrameFunction], arena[captured + WarpPortableExceptionLayout.FramePc]) != siteId)
        { return WarpPortableExceptionLayout.InvalidDescriptor; }
        uint target = TemporaryFrame(state, arena, descriptor, captured);
        if (target == uint.MaxValue) { return WarpPortableExceptionLayout.InvalidContinuation; }
        if (target == 0) { return 0; }
        uint fault = ValidateTemporarySlice(arena, descriptor, captured, site);
        if (fault != 0) { return fault; }
        for (uint ordinal = 0; ordinal < arena[site + WarpPortableExceptionLayout.SiteTemporaryCount]; ordinal++)
        {
            uint owner = descriptor + arena[site + WarpPortableExceptionLayout.SiteTemporaryStart] + ordinal * WarpPortableExceptionLayout.TemporaryOwnerWords;
            uint source = target + arena[descriptor + WarpPortableExceptionLayout.PrivateOffset] + arena[owner + WarpPortableExceptionLayout.TemporaryPrivateOffset];
            if (TemporaryReference(state, arena, source, arena[owner + WarpPortableExceptionLayout.TemporaryType]) == 0)
            { return WarpPortableExceptionLayout.InvalidReference; }
        }
        return 0;
    }

    // An already popped frame, or a runtime helper occupying its old physical
    // row, has no live source private roots. Never write that newer activation.
    // A different live source activation is an invalid captured continuation.
    private static uint TemporaryFrame(uint[] state, uint[] arena, uint descriptor, uint captured)
    {
        uint physical = arena[captured + WarpPortableExceptionLayout.FramePhysical];
        if (physical == 0 || arena[captured + WarpPortableExceptionLayout.FrameActivation] == 0 ||
            Fits(physical, arena[descriptor + WarpPortableExceptionLayout.StateStride], (uint)state.Length - WarpLogicalMachineLayout.HeaderWords) == 0)
        { return uint.MaxValue; }
        if (physical > state[WarpLogicalMachineLayout.DepthOffset]) { return 0; }
        uint target = WarpLogicalMachineLayout.HeaderWords + (physical - 1) * arena[descriptor + WarpPortableExceptionLayout.StateStride];
        if (state[target + WarpLogicalMachineLayout.FrameActivationOffset] != arena[captured + WarpPortableExceptionLayout.FrameActivation])
        {
            uint body = LookupBody(arena, descriptor, state[target]);
            return body != 0 && arena[body + WarpPortableExceptionLayout.BodyMethod] == uint.MaxValue ? 0u : uint.MaxValue;
        }
        return state[target] == arena[captured + WarpPortableExceptionLayout.FrameFunction] &&
            state[target + WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset] == arena[captured + WarpPortableExceptionLayout.FrameAliasOwnerPhysical] &&
            state[target + WarpLogicalMachineLayout.FramePrivateWordsOffset] == arena[captured + WarpPortableExceptionLayout.FramePrivateWords] ? target : uint.MaxValue;
    }

    private static uint TemporaryReference(uint[] state, uint[] arena, uint source, uint type)
    {
        if ((state[source] | state[source + 1] | state[source + 2]) == 0) { return 1; }
        uint row = RequireObject(arena, state[source], state[source + 1], state[source + 2]);
        return row != 0 && Assignable(arena, arena[row + WarpPortableHeapLayout.SlotType], type) != 0 ? 1u : 0u;
    }

    private static uint ClearTemporaryCleanup(uint[] state, uint[] arena, uint descriptor, uint worker, uint index, uint boundary)
    {
        uint record = Record(arena, descriptor, worker, index);
        for (uint ordinal = 1; ordinal <= arena[record + WarpPortableExceptionLayout.FrameCount]; ordinal++)
        {
            uint captured = Frame(arena, descriptor, worker, index, ordinal);
            if (arena[captured + WarpPortableExceptionLayout.FramePhysical] < boundary) { continue; }
            uint target = TemporaryFrame(state, arena, descriptor, captured);
            if (target == 0) { continue; }
            if (target == uint.MaxValue) { return WarpPortableExceptionLayout.InvalidContinuation; }
            uint site = Site(arena, descriptor, arena[captured + WarpPortableExceptionLayout.FrameSite]);
            for (uint field = 0; field < arena[site + WarpPortableExceptionLayout.SiteTemporaryCount]; field++)
            {
                uint owner = descriptor + arena[site + WarpPortableExceptionLayout.SiteTemporaryStart] + field * WarpPortableExceptionLayout.TemporaryOwnerWords;
                uint destination = target + arena[descriptor + WarpPortableExceptionLayout.PrivateOffset] + arena[owner + WarpPortableExceptionLayout.TemporaryPrivateOffset];
                state[destination] = 0; state[destination + 1] = 0; state[destination + 2] = 0;
            }
        }
        arena[record + WarpPortableExceptionLayout.TemporaryOwnersRetired] = 1;
        arena[record + WarpPortableExceptionLayout.TemporaryRetirementBoundary] = boundary;
        return 0;
    }
}
