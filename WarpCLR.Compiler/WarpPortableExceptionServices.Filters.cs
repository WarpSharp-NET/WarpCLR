using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableExceptionServices
{
    // Preparation changes only owned arena descriptors. The compiler commits
    // a fresh alias/driver frame and the nonlocal dispatch in one IR node.
    public static uint PrepareFilterTransfer(uint[] state, uint[] arena, uint controller, uint worker, uint run, uint dispatch, uint raise)
    {
        uint fault = Begin(arena, controller, worker, run, dispatch);
        if (fault != 0) { return fault; }
        uint descriptor = arena[WarpPortableExceptionLayout.Descriptor]; uint index = Active(arena, descriptor, worker);
        if (index == 0) { return WarpPortableExceptionLayout.InvalidPhase; }
        uint record = Record(arena, descriptor, worker, index); uint entry = Worker(arena, descriptor, worker);
        if (raise == 0 || arena[record + WarpPortableExceptionLayout.RaiseGeneration] != raise ||
            arena[record + WarpPortableExceptionLayout.TransferReady] != 0) { return WarpPortableExceptionLayout.InvalidPhase; }
        uint active = record;
        uint rejecting = arena[record + WarpPortableExceptionLayout.Action] == WarpPortableExceptionLayout.RejectFilter ? 1u : 0u;
        if (rejecting != 0)
        {
            uint parent = arena[record + WarpPortableExceptionLayout.BoundaryRecord];
            if (parent == 0 || parent > arena[descriptor + WarpPortableExceptionLayout.RecordsPerWorker] ||
                OwnedRoot(arena, descriptor, arena[record + WarpPortableExceptionLayout.RecordRoot]) == 0 ||
                OwnedRoot(arena, descriptor, arena[record + WarpPortableExceptionLayout.RecordRoot] + 1) == 0) { return WarpPortableExceptionLayout.InvalidOwnership; }
            active = Record(arena, descriptor, worker, parent);
        }
        if (arena[active + WarpPortableExceptionLayout.Phase] != WarpPortableExceptionLayout.Filtering ||
            arena[active + WarpPortableExceptionLayout.FrameCount] == 0 ||
            arena[active + WarpPortableExceptionLayout.FrameCount] > arena[descriptor + WarpPortableExceptionLayout.MaximumFrames]) { return WarpPortableExceptionLayout.InvalidPhase; }
        uint selected = arena[active + WarpPortableExceptionLayout.SelectedFrame]; uint clauseId = arena[active + WarpPortableExceptionLayout.SelectedClause];
        if (selected == 0 || selected > arena[active + WarpPortableExceptionLayout.FrameCount] ||
            clauseId == 0 || clauseId > arena[descriptor + WarpPortableExceptionLayout.ClauseCount]) { return WarpPortableExceptionLayout.InvalidDescriptor; }
        uint clause = Clause(arena, descriptor, clauseId);
        uint body = LookupBody(arena, descriptor, arena[clause + WarpPortableExceptionLayout.FilterFunction]);
        if (body == 0 || arena[body + WarpPortableExceptionLayout.BodyAliasOwner] != arena[clause + WarpPortableExceptionLayout.ClauseFunction] ||
            arena[body + WarpPortableExceptionLayout.BodyAliasPrefix] != arena[clause + WarpPortableExceptionLayout.FilterPrefix]) { return WarpPortableExceptionLayout.NeedsFilterAlias; }
        uint owned = Frame(arena, descriptor, worker, rejecting == 0 ? index : arena[record + WarpPortableExceptionLayout.BoundaryRecord], selected);
        uint top = Frame(arena, descriptor, worker, rejecting == 0 ? index : arena[record + WarpPortableExceptionLayout.BoundaryRecord],
            arena[active + WarpPortableExceptionLayout.FrameCount]);
        uint original = arena[top + WarpPortableExceptionLayout.FramePhysical];
        fault = ValidateFilterOwner(state, arena, descriptor, entry, owned, original, body);
        if (fault != 0) { return fault; }
        if (rejecting == 0 && (arena[active + WarpPortableExceptionLayout.FilterPhysical] != 0 ||
            RequireException(arena, descriptor, arena[active + WarpPortableExceptionLayout.ExceptionReference], arena[active + WarpPortableExceptionLayout.ExceptionReference + 1],
                arena[active + WarpPortableExceptionLayout.ExceptionReference + 2], arena[active + WarpPortableExceptionLayout.ExceptionExactType]) == 0)) { return WarpPortableExceptionLayout.InvalidReference; }
        if (rejecting != 0 && (arena[active + WarpPortableExceptionLayout.FilterPhysical] != original + 1 ||
            arena[active + WarpPortableExceptionLayout.FilterActivation] == 0)) { return WarpPortableExceptionLayout.InvalidContinuation; }
        return FinishFilterPreparation(state, arena, descriptor, worker, index, owned, top, body, clause, rejecting);
    }

    private static uint FinishFilterPreparation(uint[] state, uint[] arena, uint descriptor, uint worker, uint index,
        uint owned, uint top, uint body, uint clause, uint rejecting)
    {
        uint record = Record(arena, descriptor, worker, index); uint original = arena[top + WarpPortableExceptionLayout.FramePhysical];
        uint fault;
        if (rejecting != 0)
        {
            fault = ValidateTemporaryCleanup(state, arena, descriptor, worker, index, original + 1);
            if (fault != 0) { return fault; }
        }
        fault = SetFilterTransfer(state, arena, descriptor, record, owned, top, body, clause, rejecting);
        return fault == 0 && rejecting != 0 ? ClearTemporaryCleanup(state, arena, descriptor, worker, index, original + 1) : fault;
    }

    private static uint SetFilterTransfer(uint[] state, uint[] arena, uint descriptor, uint record, uint owned, uint top, uint body, uint clause, uint rejecting)
    {
        uint original = arena[top + WarpPortableExceptionLayout.FramePhysical];
        uint function = rejecting == 0 ? arena[clause + WarpPortableExceptionLayout.FilterFunction] : arena[descriptor + WarpPortableExceptionLayout.DriverFunction];
        uint pc = rejecting == 0 ? arena[clause + WarpPortableExceptionLayout.FilterPc] : arena[descriptor + WarpPortableExceptionLayout.DriverPc];
        if (ValidMachinePc(arena, descriptor, function, pc) == 0) { return WarpPortableExceptionLayout.InvalidContinuation; }
        if (rejecting != 0)
        {
            uint fault = ProjectManagedTrace(arena, descriptor, record, arena[owned + WarpPortableExceptionLayout.FramePhysical],
                arena[owned + WarpPortableExceptionLayout.FrameActivation]);
            if (fault != 0) { return fault; }
        }
        arena[record + WarpPortableExceptionLayout.FilterOriginalDepth] = original;
        arena[record + WarpPortableExceptionLayout.FilterOwnerPhysical] = arena[owned + WarpPortableExceptionLayout.FramePhysical];
        arena[record + WarpPortableExceptionLayout.FilterOwnerActivation] = arena[owned + WarpPortableExceptionLayout.FrameActivation];
        arena[record + WarpPortableExceptionLayout.FilterPrivateWords] = rejecting == 0 ? arena[body + WarpPortableExceptionLayout.BodyPrivateWords] : 0;
        arena[record + WarpPortableExceptionLayout.FilterEvaluationOffset] = arena[body + WarpPortableExceptionLayout.BodyEvaluationOffset];
        arena[record + WarpPortableExceptionLayout.FilterParentRecord] = rejecting == 0 ? 0 : arena[record + WarpPortableExceptionLayout.BoundaryRecord];
        arena[record + WarpPortableExceptionLayout.TransferPhysical] = original + 1;
        arena[record + WarpPortableExceptionLayout.TransferFunction] = function;
        arena[record + WarpPortableExceptionLayout.TransferPc] = pc;
        arena[record + WarpPortableExceptionLayout.TransferActivation] = state[WarpLogicalMachineLayout.NextActivationOffset] + 1;
        arena[record + WarpPortableExceptionLayout.TransferLogicalDepth] = arena[top + WarpPortableExceptionLayout.FrameLogicalDepth];
        arena[record + WarpPortableExceptionLayout.TransferReady] = rejecting == 0 ? 3u : 5u;
        return 0;
    }

    private static uint ValidateFilterOwner(uint[] state, uint[] arena, uint descriptor, uint worker, uint captured, uint original, uint body)
    {
        if ((uint)state.Length < WarpLogicalMachineLayout.HeaderWords || state[WarpLogicalMachineLayout.StatusOffset] != WarpLogicalMachineLayout.Runnable ||
            state[WarpLogicalMachineLayout.OwnerContextOffset] != arena[worker + WarpPortableExceptionLayout.OwnerContext] ||
            state[WarpLogicalMachineLayout.FrameStrideOffset] != arena[descriptor + WarpPortableExceptionLayout.StateStride] ||
            state[WarpLogicalMachineLayout.PrivateBaseOffset] != arena[descriptor + WarpPortableExceptionLayout.PrivateOffset] ||
            arena[descriptor + WarpPortableExceptionLayout.PrivateOffset] > arena[descriptor + WarpPortableExceptionLayout.StateStride] ||
            original == 0 || original >= state[WarpLogicalMachineLayout.DepthOffset] ||
            Fits(original + 1, arena[descriptor + WarpPortableExceptionLayout.StateStride], (uint)state.Length - WarpLogicalMachineLayout.HeaderWords) == 0)
        { return WarpPortableExceptionLayout.InvalidContinuation; }
        if (state[WarpLogicalMachineLayout.NextActivationOffset] == uint.MaxValue) { return WarpPortableExceptionLayout.GenerationExhausted; }
        uint physical = arena[captured + WarpPortableExceptionLayout.FramePhysical];
        if (physical == 0 || physical > original) { return WarpPortableExceptionLayout.InvalidContinuation; }
        uint frame = WarpLogicalMachineLayout.HeaderWords + (physical - 1) * arena[descriptor + WarpPortableExceptionLayout.StateStride];
        if (state[frame] != arena[body + WarpPortableExceptionLayout.BodyAliasOwner] ||
            state[frame + WarpLogicalMachineLayout.FrameActivationOffset] != arena[captured + WarpPortableExceptionLayout.FrameActivation] ||
            state[frame + WarpLogicalMachineLayout.FramePrivateWordsOffset] != arena[captured + WarpPortableExceptionLayout.FramePrivateWords] ||
            arena[body + WarpPortableExceptionLayout.BodyEvaluationOffset] < arena[body + WarpPortableExceptionLayout.BodyAliasPrefix] ||
            arena[body + WarpPortableExceptionLayout.BodyPrivateWords] < arena[body + WarpPortableExceptionLayout.BodyEvaluationOffset] + 3 ||
            arena[body + WarpPortableExceptionLayout.BodyPrivateWords] > arena[descriptor + WarpPortableExceptionLayout.StateStride] - arena[descriptor + WarpPortableExceptionLayout.PrivateOffset])
        { return WarpPortableExceptionLayout.InvalidContinuation; }
        return 0;
    }

    public static uint PrepareFilterEnd(uint[] state, uint[] arena, uint controller, uint worker, uint run, uint dispatch, uint raise,
        uint decision, uint sourceFunction, uint sourceOffset, uint sourceOpcode)
    {
        uint fault = Begin(arena, controller, worker, run, dispatch);
        if (fault != 0) { return fault; }
        uint descriptor = arena[WarpPortableExceptionLayout.Descriptor]; uint index = Active(arena, descriptor, worker);
        if (index == 0 || sourceOpcode != 0xFE11) { return WarpPortableExceptionLayout.UnknownSourceSite; }
        uint record = Record(arena, descriptor, worker, index); uint clauseId = arena[record + WarpPortableExceptionLayout.SelectedClause];
        if (raise == 0 || arena[record + WarpPortableExceptionLayout.RaiseGeneration] != raise ||
            arena[record + WarpPortableExceptionLayout.Phase] != WarpPortableExceptionLayout.Filtering ||
            arena[record + WarpPortableExceptionLayout.TransferReady] != 0 || (arena[record + WarpPortableExceptionLayout.Flags] & 2) == 0 ||
            clauseId == 0 || clauseId > arena[descriptor + WarpPortableExceptionLayout.ClauseCount]) { return WarpPortableExceptionLayout.InvalidPhase; }
        uint clause = Clause(arena, descriptor, clauseId); uint physical = arena[record + WarpPortableExceptionLayout.FilterPhysical];
        uint body = LookupBody(arena, descriptor, sourceFunction);
        uint site = LookupSite(arena, descriptor, sourceFunction, sourceOffset, sourceOpcode);
        if (body == 0 || site == 0 || sourceFunction != arena[clause + WarpPortableExceptionLayout.FilterFunction] ||
            sourceOffset < arena[clause + WarpPortableExceptionLayout.FilterStart] || sourceOffset >= arena[clause + WarpPortableExceptionLayout.HandlerStart] ||
            physical == 0 || physical != arena[record + WarpPortableExceptionLayout.FilterOriginalDepth] + 1 ||
            (uint)state.Length < WarpLogicalMachineLayout.HeaderWords || physical >= state[WarpLogicalMachineLayout.DepthOffset] ||
            Fits(physical, arena[descriptor + WarpPortableExceptionLayout.StateStride], (uint)state.Length - WarpLogicalMachineLayout.HeaderWords) == 0)
        { return WarpPortableExceptionLayout.InvalidContinuation; }
        uint alias = WarpLogicalMachineLayout.HeaderWords + (physical - 1) * arena[descriptor + WarpPortableExceptionLayout.StateStride];
        if (state[alias] != sourceFunction || state[alias + WarpLogicalMachineLayout.FrameActivationOffset] != arena[record + WarpPortableExceptionLayout.FilterActivation] ||
            LookupPc(arena, descriptor, state[alias], state[alias + 1]) != site ||
            state[WarpLogicalMachineLayout.OwnerContextOffset] != arena[Worker(arena, descriptor, worker) + WarpPortableExceptionLayout.OwnerContext])
        { return WarpPortableExceptionLayout.InvalidContinuation; }
        if (state[WarpLogicalMachineLayout.NextActivationOffset] == uint.MaxValue) { return WarpPortableExceptionLayout.GenerationExhausted; }
        arena[record + WarpPortableExceptionLayout.FilterDecision] = decision == 0 ? 0u : 1u;
        arena[record + WarpPortableExceptionLayout.TransferFunction] = arena[descriptor + WarpPortableExceptionLayout.DriverFunction];
        arena[record + WarpPortableExceptionLayout.TransferPc] = arena[descriptor + WarpPortableExceptionLayout.DriverPc];
        arena[record + WarpPortableExceptionLayout.TransferPhysical] = physical;
        arena[record + WarpPortableExceptionLayout.TransferActivation] = state[WarpLogicalMachineLayout.NextActivationOffset] + 1;
        arena[record + WarpPortableExceptionLayout.FilterPrivateWords] = 0;
        arena[record + WarpPortableExceptionLayout.TransferReady] = 4;
        return 0;
    }
}
