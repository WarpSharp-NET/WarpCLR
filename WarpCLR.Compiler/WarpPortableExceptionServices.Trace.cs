using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableExceptionServices
{
    private static uint ValidateCapturedAlias(uint[] state, uint[] arena, uint descriptor, uint physical, uint frame, uint body)
    {
        uint function = arena[body + WarpPortableExceptionLayout.BodyAliasOwner];
        if (function == uint.MaxValue)
        {
            return state[frame + WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset] == 0 &&
                state[frame + WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset] == 0 ? 1u : 0u;
        }
        uint owner = state[frame + WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset];
        if (owner == 0 || owner > physical || state[frame + WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset] == 0) { return 0; }
        uint source = WarpLogicalMachineLayout.HeaderWords + (owner - 1) * arena[descriptor + WarpPortableExceptionLayout.StateStride];
        return state[source] == function &&
            state[source + WarpLogicalMachineLayout.FrameActivationOffset] == state[frame + WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset] &&
            state[source + WarpLogicalMachineLayout.FramePrivateWordsOffset] >= arena[body + WarpPortableExceptionLayout.BodyAliasPrefix] ? 1u : 0u;
    }

    private static uint WriteLogicalFrames(uint[] arena, uint descriptor, uint worker, uint index, uint record, uint payload)
    {
        uint count = 0;
        for (uint frame = 1; frame <= arena[record + WarpPortableExceptionLayout.FrameCount]; frame++)
        {
            uint captured = Frame(arena, descriptor, worker, index, frame);
            uint body = LookupBody(arena, descriptor, arena[captured + WarpPortableExceptionLayout.FrameFunction]);
            if (arena[body + WarpPortableExceptionLayout.BodyCountsDepth] == 0) { continue; }
            uint site = Site(arena, descriptor, LogicalFrameSite(arena, descriptor, worker, index, record, captured));
            uint destination = payload + arena[payload + WarpPortableExceptionTraceLayout.RawStart] + count * WarpPortableExceptionTraceLayout.FrameWords;
            arena[destination + WarpPortableExceptionTraceLayout.Method] = arena[site + WarpPortableExceptionLayout.SiteMethod];
            arena[destination + WarpPortableExceptionTraceLayout.Offset] = arena[site + WarpPortableExceptionLayout.SiteOffset];
            arena[destination + WarpPortableExceptionTraceLayout.Physical] = arena[captured + WarpPortableExceptionLayout.FramePhysical];
            arena[destination + WarpPortableExceptionTraceLayout.Activation] = arena[captured + WarpPortableExceptionLayout.FrameActivation];
            count++;
        }
        return count;
    }

    private static uint LogicalFrameSite(uint[] arena, uint descriptor, uint worker, uint index, uint record, uint captured)
    {
        uint site = arena[captured + WarpPortableExceptionLayout.FrameSite];
        for (uint frame = 1; frame <= arena[record + WarpPortableExceptionLayout.FrameCount]; frame++)
        {
            uint alias = Frame(arena, descriptor, worker, index, frame);
            if (arena[alias + WarpPortableExceptionLayout.FrameAliasOwnerPhysical] != arena[captured + WarpPortableExceptionLayout.FramePhysical]) { continue; }
            uint body = LookupBody(arena, descriptor, arena[alias + WarpPortableExceptionLayout.FrameFunction]);
            if (arena[body + WarpPortableExceptionLayout.BodyAliasOwner] == arena[captured + WarpPortableExceptionLayout.FrameFunction])
            { site = arena[alias + WarpPortableExceptionLayout.FrameSite]; }
        }
        return site;
    }
}
