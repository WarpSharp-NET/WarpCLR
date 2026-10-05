using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableExceptionServices
{
    internal static uint ValidateFaultEnvironment(uint[] arena, uint controller, uint worker, uint run, uint dispatch) => Begin(arena, controller, worker, run, dispatch);

    internal static uint FaultPcMatches(uint[] arena, uint function, uint pc, uint offset, uint opcode)
    {
        uint descriptor = arena[WarpPortableExceptionLayout.Descriptor]; uint site = LookupPc(arena, descriptor, function, pc);
        return site != 0 && site == LookupSite(arena, descriptor, function, offset, opcode) ? 1u : 0u;
    }

    internal static uint ValidateFaultCensus(uint[] state, uint[] arena, uint descriptor, uint worker, uint index,
        uint sourcePhysical, uint sourceActivation, uint sourceFunction, uint originalFunction, uint offset, uint opcode, uint effect,
        uint ownerPhysical, uint ownerActivation, uint raised)
    {
        uint entry = Worker(arena, descriptor, worker);
        if (index == 0 || index > arena[descriptor + WarpPortableExceptionLayout.RecordsPerWorker]) { return WarpPortableExceptionLayout.InvalidTicket; }
        uint record = Record(arena, descriptor, worker, index);
        if (raised != 0 ? arena[entry + WarpPortableExceptionLayout.ActiveRecord] != index ||
            arena[record + WarpPortableExceptionLayout.Phase] != WarpPortableExceptionLayout.Searching :
            arena[entry + WarpPortableExceptionLayout.PreparedRecord] != index || arena[record + WarpPortableExceptionLayout.Phase] != WarpPortableExceptionLayout.Prepared)
        { return WarpPortableExceptionLayout.InvalidPhase; }
        uint count = ValidateCapture(state, arena, descriptor, entry);
        if (count == 0 || count != arena[record + WarpPortableExceptionLayout.FrameCount] ||
            state[WarpLogicalMachineLayout.OwnerContextOffset] != arena[entry + WarpPortableExceptionLayout.OwnerContext])
        { return WarpPortableExceptionLayout.InvalidContinuation; }
        uint site = LookupSite(arena, descriptor, sourceFunction, offset, opcode);
        if (site == 0 || effect >= arena[Site(arena, descriptor, site) + WarpPortableExceptionLayout.SiteEffectCount])
        { return WarpPortableExceptionLayout.UnknownSourceSite; }
        uint top = Frame(arena, descriptor, worker, index, count);
        if (arena[top + WarpPortableExceptionLayout.FramePhysical] != sourcePhysical ||
            arena[top + WarpPortableExceptionLayout.FrameActivation] != sourceActivation ||
            arena[top + WarpPortableExceptionLayout.FrameFunction] != sourceFunction || arena[top + WarpPortableExceptionLayout.FrameSite] != site ||
            sourcePhysical == 0 || sourcePhysical > state[WarpLogicalMachineLayout.DepthOffset] || ownerPhysical == 0 || ownerPhysical > sourcePhysical ||
            sourceActivation == 0 || ownerActivation == 0) { return WarpPortableExceptionLayout.InvalidContinuation; }
        uint stride = arena[descriptor + WarpPortableExceptionLayout.StateStride];
        uint source = WarpLogicalMachineLayout.HeaderWords + (sourcePhysical - 1) * stride;
        uint owner = WarpLogicalMachineLayout.HeaderWords + (ownerPhysical - 1) * stride;
        uint body = LookupBody(arena, descriptor, sourceFunction);
        if (body == 0 || state[owner] != originalFunction || state[owner + WarpLogicalMachineLayout.FrameActivationOffset] != ownerActivation)
        { return WarpPortableExceptionLayout.InvalidContinuation; }
        if (arena[body + WarpPortableExceptionLayout.BodyAliasOwner] == uint.MaxValue)
        {
            if (sourceFunction != originalFunction || sourcePhysical != ownerPhysical || sourceActivation != ownerActivation)
            { return WarpPortableExceptionLayout.InvalidOwnership; }
        }
        else if (arena[body + WarpPortableExceptionLayout.BodyAliasOwner] != originalFunction ||
            state[source + WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset] != ownerPhysical ||
            state[source + WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset] != ownerActivation)
        { return WarpPortableExceptionLayout.InvalidOwnership; }
        uint capturedCount = 0, logical = 0;
        for (uint physical = 0; physical < state[WarpLogicalMachineLayout.DepthOffset]; physical++)
        {
            uint frame = WarpLogicalMachineLayout.HeaderWords + physical * stride;
            body = LookupBody(arena, descriptor, state[frame]);
            if (arena[body + WarpPortableExceptionLayout.BodyMethod] == uint.MaxValue) { continue; }
            logical += arena[body + WarpPortableExceptionLayout.BodyCountsDepth];
            uint captured = Frame(arena, descriptor, worker, index, ++capturedCount);
            if (arena[captured + WarpPortableExceptionLayout.FramePhysical] != physical + 1 ||
                arena[captured + WarpPortableExceptionLayout.FrameActivation] != state[frame + WarpLogicalMachineLayout.FrameActivationOffset] ||
                arena[captured + WarpPortableExceptionLayout.FrameFunction] != state[frame] ||
                arena[captured + WarpPortableExceptionLayout.FrameSite] != LookupPc(arena, descriptor, state[frame], state[frame + 1]) ||
                arena[captured + WarpPortableExceptionLayout.FramePrivateWords] != state[frame + WarpLogicalMachineLayout.FramePrivateWordsOffset] ||
                arena[captured + WarpPortableExceptionLayout.FrameLogicalDepth] != logical ||
                arena[captured + WarpPortableExceptionLayout.FrameAliasOwnerPhysical] != state[frame + WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset] ||
                arena[body + WarpPortableExceptionLayout.BodyAliasOwner] != uint.MaxValue &&
                    FaultAliasIsLive(arena, descriptor, worker, physical + 1, state[frame + WarpLogicalMachineLayout.FrameActivationOffset],
                        state[frame + WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset], state[frame + WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset]) == 0)
            { return WarpPortableExceptionLayout.InvalidContinuation; }
        }
        return 0;
    }

    private static uint FaultAliasIsLive(uint[] arena, uint descriptor, uint worker, uint physical, uint activation, uint ownerPhysical, uint ownerActivation)
    {
        for (uint index = 1; index <= arena[descriptor + WarpPortableExceptionLayout.RecordsPerWorker]; index++)
        {
            uint record = Record(arena, descriptor, worker, index);
            if (arena[record + WarpPortableExceptionLayout.Phase] == WarpPortableExceptionLayout.Filtering &&
                arena[record + WarpPortableExceptionLayout.FilterPhysical] == physical && arena[record + WarpPortableExceptionLayout.FilterActivation] == activation &&
                arena[record + WarpPortableExceptionLayout.FilterOwnerPhysical] == ownerPhysical && arena[record + WarpPortableExceptionLayout.FilterOwnerActivation] == ownerActivation)
            { return 1; }
        }
        return 0;
    }
}
