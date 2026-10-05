using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableExceptionServices
{
    // This validates and prepares a transfer in the executing source bank. It
    // never changes depth/PC before an ordinary helper return. The compiler then
    // commits those control words and StateDispatch in one indivisible IR node.
    public static uint ApplyAction(uint[] state, uint[] arena, uint controller, uint worker, uint run, uint dispatch, uint raise)
    {
        uint fault = Begin(arena, controller, worker, run, dispatch);
        if (fault != 0) { return fault; }
        uint descriptor = arena[WarpPortableExceptionLayout.Descriptor]; uint index = Active(arena, descriptor, worker);
        if (index == 0) { return WarpPortableExceptionLayout.InvalidPhase; }
        uint entry = Worker(arena, descriptor, worker); uint record = Record(arena, descriptor, worker, index);
        if (raise == 0 || arena[record + WarpPortableExceptionLayout.RaiseGeneration] != raise ||
            arena[record + WarpPortableExceptionLayout.TransferReady] != 0 ||
            (uint)state.Length < WarpLogicalMachineLayout.HeaderWords ||
            state[WarpLogicalMachineLayout.StatusOffset] != WarpLogicalMachineLayout.Runnable ||
            state[WarpLogicalMachineLayout.OwnerContextOffset] != arena[entry + WarpPortableExceptionLayout.OwnerContext] ||
            state[WarpLogicalMachineLayout.FrameStrideOffset] != arena[descriptor + WarpPortableExceptionLayout.StateStride] ||
            state[WarpLogicalMachineLayout.PrivateBaseOffset] != arena[descriptor + WarpPortableExceptionLayout.PrivateOffset]) { return WarpPortableExceptionLayout.InvalidContinuation; }
        uint action = arena[record + WarpPortableExceptionLayout.Action];
        if (action == WarpPortableExceptionLayout.RunFilter || action == WarpPortableExceptionLayout.RejectFilter)
        {
            return PrepareFilterTransfer(state, arena, controller, worker, run, dispatch, raise);
        }
        if (action == WarpPortableExceptionLayout.Escaped) { return PrepareEscapedOwners(state, arena, descriptor, worker, index); }
        if (action == WarpPortableExceptionLayout.ResourceTermination) { return WarpPortableExceptionLayout.NeedsTerminalCapability; }
        uint frame = action == WarpPortableExceptionLayout.EnterCatch ? arena[record + WarpPortableExceptionLayout.SelectedFrame] : arena[record + WarpPortableExceptionLayout.UnwindFrame];
        if (frame == 0 || frame > arena[record + WarpPortableExceptionLayout.FrameCount]) { return WarpPortableExceptionLayout.InvalidContinuation; }
        uint clauseId = action == WarpPortableExceptionLayout.EnterCatch ? arena[record + WarpPortableExceptionLayout.SelectedClause] : arena[record + WarpPortableExceptionLayout.CleanupClause];
        uint pc = arena[record + WarpPortableExceptionLayout.LeaveTarget];
        if (action != WarpPortableExceptionLayout.ResumeLeave)
        {
            if (clauseId == 0 || clauseId > arena[descriptor + WarpPortableExceptionLayout.ClauseCount]) { return WarpPortableExceptionLayout.InvalidContinuation; }
            pc = arena[Clause(arena, descriptor, clauseId) + WarpPortableExceptionLayout.HandlerPc];
        }
        return PrepareActionTransfer(state, arena, descriptor, worker, index, frame, pc, action, clauseId);
    }

    private static uint PrepareActionTransfer(uint[] state, uint[] arena, uint descriptor, uint worker, uint index,
        uint frame, uint pc, uint action, uint clauseId)
    {
        uint record = Record(arena, descriptor, worker, index); uint captured = Frame(arena, descriptor, worker, index, frame);
        uint fault = ValidateTransfer(state, arena, descriptor, record, captured, pc, action, clauseId);
        if (fault != 0) { return fault; }
        uint boundary = arena[captured + WarpPortableExceptionLayout.FramePhysical];
        if (action == WarpPortableExceptionLayout.EnterCatch)
        {
            fault = ValidateTemporaryCleanup(state, arena, descriptor, worker, index, boundary);
            if (fault != 0) { return fault; }
        }
        fault = PruneTransferScopes(state, arena, descriptor, worker, captured, pc);
        if (fault != 0) { return fault; }
        if (action == WarpPortableExceptionLayout.EnterCatch)
        {
            fault = WriteCatchReference(state, arena, descriptor, captured, clauseId, record);
            if (fault != 0) { return fault; }
            fault = ClearTemporaryCleanup(state, arena, descriptor, worker, index, boundary);
            if (fault != 0) { return fault; }
        }
        PrepareTransfer(arena, record, captured, frame, pc);
        return 0;
    }

    private static uint PrepareEscapedOwners(uint[] state, uint[] arena, uint descriptor, uint worker, uint index)
    {
        uint fault = ValidateTemporaryCleanup(state, arena, descriptor, worker, index, 1);
        if (fault != 0) { return fault; }
        uint record = Record(arena, descriptor, worker, index);
        fault = PrepareEscaped(arena, descriptor, record);
        return fault == 0 ? ClearTemporaryCleanup(state, arena, descriptor, worker, index, 1) : fault;
    }

    private static uint WriteCatchReference(uint[] state, uint[] arena, uint descriptor, uint captured, uint clauseId, uint record)
    {
        uint fault = ProjectManagedTrace(arena, descriptor, record, arena[captured + WarpPortableExceptionLayout.FramePhysical],
            arena[captured + WarpPortableExceptionLayout.FrameActivation]);
        if (fault != 0) { return fault; }
        uint target = WarpLogicalMachineLayout.HeaderWords + (arena[captured + WarpPortableExceptionLayout.FramePhysical] - 1) * arena[descriptor + WarpPortableExceptionLayout.StateStride] +
            arena[descriptor + WarpPortableExceptionLayout.PrivateOffset] + arena[Clause(arena, descriptor, clauseId) + WarpPortableExceptionLayout.ExceptionPrivateOffset];
        state[target] = arena[record + WarpPortableExceptionLayout.ExceptionReference];
        state[target + 1] = arena[record + WarpPortableExceptionLayout.ExceptionReference + 1];
        state[target + 2] = arena[record + WarpPortableExceptionLayout.ExceptionReference + 2];
        return 0;
    }

    private static uint ValidateTransfer(uint[] state, uint[] arena, uint descriptor, uint record, uint captured, uint pc, uint action, uint clauseId)
    {
        if (action != WarpPortableExceptionLayout.EnterCatch && action != WarpPortableExceptionLayout.RunFinally &&
            action != WarpPortableExceptionLayout.RunFault && action != WarpPortableExceptionLayout.ResumeLeave) { return WarpPortableExceptionLayout.InvalidPhase; }
        uint physical = arena[captured + WarpPortableExceptionLayout.FramePhysical]; uint stride = arena[descriptor + WarpPortableExceptionLayout.StateStride];
        if (physical == 0 || physical > state[WarpLogicalMachineLayout.DepthOffset] ||
            Fits(physical, stride, (uint)state.Length - WarpLogicalMachineLayout.HeaderWords) == 0) { return WarpPortableExceptionLayout.InvalidContinuation; }
        uint target = WarpLogicalMachineLayout.HeaderWords + (physical - 1) * stride;
        if (state[target] != arena[captured + WarpPortableExceptionLayout.FrameFunction] ||
            state[target + WarpLogicalMachineLayout.FrameActivationOffset] != arena[captured + WarpPortableExceptionLayout.FrameActivation] ||
            state[target + WarpLogicalMachineLayout.FramePrivateWordsOffset] != arena[captured + WarpPortableExceptionLayout.FramePrivateWords] ||
            ValidMachinePc(arena, descriptor, state[target], pc) == 0) { return WarpPortableExceptionLayout.InvalidContinuation; }
        if (action == WarpPortableExceptionLayout.EnterCatch)
        {
            uint clause = Clause(arena, descriptor, clauseId); uint offset = arena[clause + WarpPortableExceptionLayout.ExceptionPrivateOffset];
            if (offset > arena[captured + WarpPortableExceptionLayout.FramePrivateWords] || arena[captured + WarpPortableExceptionLayout.FramePrivateWords] - offset < 3 ||
                arena[descriptor + WarpPortableExceptionLayout.PrivateOffset] > stride ||
                arena[captured + WarpPortableExceptionLayout.FramePrivateWords] > stride - arena[descriptor + WarpPortableExceptionLayout.PrivateOffset] ||
                RequireException(arena, descriptor, arena[record + WarpPortableExceptionLayout.ExceptionReference], arena[record + WarpPortableExceptionLayout.ExceptionReference + 1],
                    arena[record + WarpPortableExceptionLayout.ExceptionReference + 2], arena[record + WarpPortableExceptionLayout.ExceptionExactType]) == 0) { return WarpPortableExceptionLayout.InvalidReference; }
        }
        return 0;
    }

    private static uint PrepareTransfer(uint[] arena, uint record, uint captured, uint frame, uint pc)
    {
        arena[record + WarpPortableExceptionLayout.TransferPhysical] = arena[captured + WarpPortableExceptionLayout.FramePhysical];
        arena[record + WarpPortableExceptionLayout.TransferPc] = pc;
        arena[record + WarpPortableExceptionLayout.TransferFunction] = arena[captured + WarpPortableExceptionLayout.FrameFunction];
        arena[record + WarpPortableExceptionLayout.TransferLogicalDepth] = arena[captured + WarpPortableExceptionLayout.FrameLogicalDepth];
        arena[record + WarpPortableExceptionLayout.TransferActivation] = arena[captured + WarpPortableExceptionLayout.FrameActivation];
        arena[record + WarpPortableExceptionLayout.TransferReady] = 1;
        return 0;
    }

    // Called only after the generated atomic control commit. Catch contexts keep
    // their exact object and original trace until their lexical activation ends.
    public static uint AcknowledgeTransfer(uint[] state, uint[] arena, uint controller, uint worker, uint run, uint dispatch, uint raise)
    {
        uint fault = Begin(arena, controller, worker, run, dispatch);
        if (fault != 0) { return fault; }
        uint descriptor = arena[WarpPortableExceptionLayout.Descriptor]; uint index = Active(arena, descriptor, worker);
        if (index == 0) { return WarpPortableExceptionLayout.InvalidPhase; }
        uint record = Record(arena, descriptor, worker, index); uint entry = Worker(arena, descriptor, worker);
        if ((uint)state.Length < WarpLogicalMachineLayout.HeaderWords || raise == 0 || arena[record + WarpPortableExceptionLayout.RaiseGeneration] != raise || arena[record + WarpPortableExceptionLayout.TransferReady] != 1 ||
            state[WarpLogicalMachineLayout.OwnerContextOffset] != arena[entry + WarpPortableExceptionLayout.OwnerContext]) { return WarpPortableExceptionLayout.InvalidPhase; }
        uint physical = arena[record + WarpPortableExceptionLayout.TransferPhysical]; uint stride = arena[descriptor + WarpPortableExceptionLayout.StateStride];
        if (physical == 0 || physical > state[WarpLogicalMachineLayout.DepthOffset] || Fits(physical, stride, (uint)state.Length - WarpLogicalMachineLayout.HeaderWords) == 0) { return WarpPortableExceptionLayout.InvalidContinuation; }
        uint target = WarpLogicalMachineLayout.HeaderWords + (physical - 1) * stride;
        if (state[target] != arena[record + WarpPortableExceptionLayout.TransferFunction] ||
            LookupPc(arena, descriptor, state[target], state[target + 1]) != LookupPc(arena, descriptor, state[target], arena[record + WarpPortableExceptionLayout.TransferPc]) ||
            state[target + WarpLogicalMachineLayout.FrameActivationOffset] != arena[record + WarpPortableExceptionLayout.TransferActivation]) { return WarpPortableExceptionLayout.InvalidContinuation; }
        uint action = arena[record + WarpPortableExceptionLayout.Action];
        if (action == WarpPortableExceptionLayout.EnterCatch)
        {
            arena[record + WarpPortableExceptionLayout.Phase] = WarpPortableExceptionLayout.Caught;
            arena[record + WarpPortableExceptionLayout.CaughtFrame] = physical;
            arena[record + WarpPortableExceptionLayout.CaughtActivation] = arena[record + WarpPortableExceptionLayout.TransferActivation];
            arena[record + WarpPortableExceptionLayout.CaughtClause] = arena[record + WarpPortableExceptionLayout.SelectedClause];
            arena[entry + WarpPortableExceptionLayout.ActiveRecord] = arena[record + WarpPortableExceptionLayout.ParentRecord];
        }
        else if (action == WarpPortableExceptionLayout.ResumeLeave)
        {
            arena[entry + WarpPortableExceptionLayout.ActiveRecord] = arena[record + WarpPortableExceptionLayout.ParentRecord];
            return ClearRecord(arena, descriptor, record);
        }
        else { arena[record + WarpPortableExceptionLayout.Flags] |= 2; }
        arena[record + WarpPortableExceptionLayout.TransferReady] = 0;
        return 0;
    }
}
