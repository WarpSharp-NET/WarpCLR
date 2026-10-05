using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableSourceFaultServices
{
    private static uint ValidateTicket(uint[] state, uint[] arena,
        uint controller, uint worker, uint run, uint dispatch, uint collectionEpoch,
        uint preparedIndex, uint ticketGeneration, uint nonce0, uint nonce1, uint nonce2, uint nonce3,
        uint sourceFunction, uint sourceOffset, uint sourceOpcode, uint effectIndex, uint factoryRow, uint faultDescriptor,
        uint sourcePhysical, uint sourceActivation, uint sourceBodyFunction, uint ownerPhysical, uint ownerActivation, uint rootRevision, uint stage)
    {
        if ((uint)state.Length < WarpLogicalMachineLayout.HeaderWords) { return WarpPortableExceptionLayout.InvalidContinuation; }
        uint fault = WarpPortableExceptionServices.ValidateFaultEnvironment(arena, controller, worker, run, dispatch);
        if (fault != 0) { return fault; }
        uint pool = Descriptor(arena);
        if (pool == 0) { return WarpPortableExceptionLayout.InvalidDescriptor; }
        if (preparedIndex >= arena[pool + WarpPortableSourceFaultFactoryLayout.PreparedCount] || factoryRow == 0 ||
            factoryRow > arena[pool + WarpPortableSourceFaultFactoryLayout.RowCount]) { return WarpPortableExceptionLayout.InvalidTicket; }
        uint record = Prepared(arena, pool, preparedIndex), row = Factory(arena, pool, factoryRow);
        if (arena[record + WarpPortableSourceFaultFactoryLayout.PreparedState] !=
            (stage == 0 ? WarpPortableSourceFaultFactoryLayout.Granted : WarpPortableSourceFaultFactoryLayout.Taken))
        { return WarpPortableExceptionLayout.InvalidPhase; }
        if (ValidateTicketIdentity(state, arena, record, controller, worker, run, dispatch, collectionEpoch, ticketGeneration,
            nonce0, nonce1, nonce2, nonce3, sourcePhysical, sourceActivation, sourceBodyFunction, ownerPhysical, ownerActivation, rootRevision) == 0)
        { return WarpPortableExceptionLayout.InvalidTicket; }
        if (arena[record + WarpPortableSourceFaultFactoryLayout.PreparedFactoryRow] != factoryRow)
        { return WarpPortableExceptionLayout.UnknownSourceSite; }
        return ValidateTicketSite(state, arena, pool, record, row, worker, collectionEpoch, rootRevision, preparedIndex,
            sourceFunction, sourceOffset, sourceOpcode, effectIndex, faultDescriptor, sourcePhysical, sourceActivation, sourceBodyFunction, ownerPhysical, ownerActivation, stage);
    }

    private static uint ValidateTicketIdentity(uint[] state, uint[] arena, uint record,
        uint controller, uint worker, uint run, uint dispatch, uint collectionEpoch, uint ticketGeneration,
        uint nonce0, uint nonce1, uint nonce2, uint nonce3, uint sourcePhysical, uint sourceActivation, uint sourceBodyFunction,
        uint ownerPhysical, uint ownerActivation, uint rootRevision)
    {
        return ticketGeneration != 0 && (nonce0 | nonce1 | nonce2 | nonce3) != 0 && rootRevision != 0 &&
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedTicketGeneration] == ticketGeneration &&
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedWorker] == worker &&
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedRun] == run &&
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedDispatch] == dispatch &&
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedCollectionEpoch] == collectionEpoch &&
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedController] == controller &&
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedNonce] == nonce0 &&
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedNonce + 1] == nonce1 &&
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedNonce + 2] == nonce2 &&
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedNonce + 3] == nonce3 &&
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedOwnerContext] == state[WarpLogicalMachineLayout.OwnerContextOffset] &&
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedOwnerDepth] == ownerPhysical &&
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedOwnerActivation] == ownerActivation &&
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedSourcePhysical] == sourcePhysical &&
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedSourceActivation] == sourceActivation &&
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedSourceBodyFunction] == sourceBodyFunction &&
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedRootRevision] == rootRevision ? 1u : 0u;
    }

    private static uint ValidateTicketSite(uint[] state, uint[] arena, uint pool, uint record, uint row,
        uint worker, uint collectionEpoch, uint rootRevision, uint preparedIndex,
        uint sourceFunction, uint sourceOffset, uint sourceOpcode, uint effectIndex, uint faultDescriptor,
        uint sourcePhysical, uint sourceActivation, uint sourceBodyFunction, uint ownerPhysical, uint ownerActivation, uint stage)
    {
        if (sourceFunction == 0 || sourceOpcode > 0xFFFFu || arena[record + WarpPortableSourceFaultFactoryLayout.PreparedFunction] != sourceFunction ||
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedOffset] != sourceOffset ||
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedOpcode] != sourceOpcode ||
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedEffect] != effectIndex ||
            arena[row + WarpPortableSourceFaultFactoryLayout.RowId] != arena[record + WarpPortableSourceFaultFactoryLayout.PreparedFactoryRow] ||
            arena[row + WarpPortableSourceFaultFactoryLayout.RowFunction] != sourceFunction ||
            arena[row + WarpPortableSourceFaultFactoryLayout.RowOffset] != sourceOffset ||
            arena[row + WarpPortableSourceFaultFactoryLayout.RowOpcode] != sourceOpcode ||
            arena[row + WarpPortableSourceFaultFactoryLayout.RowEffect] != effectIndex ||
            arena[row + WarpPortableSourceFaultFactoryLayout.RowFaultDescriptor] != faultDescriptor)
        { return WarpPortableExceptionLayout.UnknownSourceSite; }
        uint descriptor = arena[WarpPortableExceptionLayout.Descriptor];
        if (HashMatches(arena, pool + WarpPortableSourceFaultFactoryLayout.AdmittedProgramHash,
            record + WarpPortableSourceFaultFactoryLayout.PreparedProgramHash) == 0 ||
            HashMatches(arena, pool + WarpPortableSourceFaultFactoryLayout.AdmittedExceptionPlanHash,
                record + WarpPortableSourceFaultFactoryLayout.PreparedExceptionPlanHash) == 0 ||
            HashMatches(arena, descriptor + WarpPortableExceptionLayout.PlanHash,
                record + WarpPortableSourceFaultFactoryLayout.PreparedExceptionPlanHash) == 0 ||
            HashMatches(arena, pool + WarpPortableSourceFaultFactoryLayout.PlanHash,
                record + WarpPortableSourceFaultFactoryLayout.PreparedPoolPlanHash) == 0 ||
            HashMatches(arena, row + WarpPortableSourceFaultFactoryLayout.RowOperationHash,
                record + WarpPortableSourceFaultFactoryLayout.PreparedOperationHash) == 0)
        { return WarpPortableExceptionLayout.InvalidTicket; }
        uint raised = arena[record + WarpPortableSourceFaultFactoryLayout.PreparedRaiseGeneration];
        if (stage == 2 ? raised == 0 : raised != 0) { return WarpPortableExceptionLayout.InvalidPhase; }
        uint fault = ValidateRootPublication(arena, worker, collectionEpoch, rootRevision, sourceBodyFunction, sourceOffset, sourceOpcode);
        if (fault != 0) { return fault; }
        fault = ValidatePreparedData(arena, pool, record, row, preparedIndex, stage);
        if (fault != 0) { return fault; }
        uint index = arena[record + WarpPortableSourceFaultFactoryLayout.PreparedExceptionRecord];
        fault = WarpPortableExceptionServices.ValidateFaultOperationTable(arena, sourceBodyFunction, sourceOffset, sourceOpcode, effectIndex,
            arena[row + WarpPortableSourceFaultFactoryLayout.RowExceptionType], faultDescriptor);
        if (fault != 0) { return fault; }
        fault = WarpPortableExceptionServices.ValidateFaultCensus(state, arena, descriptor, worker, index,
            sourcePhysical, sourceActivation, sourceBodyFunction, sourceFunction, sourceOffset, sourceOpcode, effectIndex,
            ownerPhysical, ownerActivation, stage == 2 ? 1u : 0u);
        if (fault != 0) { return fault; }
        uint exception = ExceptionRecord(arena, descriptor, worker, index);
        uint root = arena[record + WarpPortableSourceFaultFactoryLayout.PreparedExceptionRoot];
        if (root != arena[exception + WarpPortableExceptionLayout.RecordRoot] || root >= arena[WarpPortableHeapLayout.RootCount] ||
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedExceptionRootGeneration] == 0 ||
            ValidRoot(arena, root, arena[record + WarpPortableSourceFaultFactoryLayout.PreparedExceptionRootGeneration]) == 0 ||
            ValidRoot(arena, root + 1, arena[record + WarpPortableSourceFaultFactoryLayout.PreparedExceptionRootGeneration]) == 0)
        { return WarpPortableExceptionLayout.InvalidOwnership; }
        uint reference = record + WarpPortableSourceFaultFactoryLayout.PreparedReference;
        if (stage != 2)
        {
            return NullRoot(arena, root) != 0 && NullRoot(arena, root + 1) != 0 ? 0 : WarpPortableExceptionLayout.InvalidOwnership;
        }
        return WarpPortableExceptionServices.ValidateFaultPublication(arena, descriptor, worker, index, raised,
            sourceBodyFunction, sourceOffset, sourceOpcode, effectIndex, arena[reference], arena[reference + 1], arena[reference + 2],
            arena[row + WarpPortableSourceFaultFactoryLayout.RowExceptionType], faultDescriptor);
    }
}
