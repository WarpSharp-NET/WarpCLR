namespace WarpCLR.Compiler;

// These helpers verify a privately issued ticket. They never issue one. The
// parent registry admits their exact IR and publishes Granted last; plausible
// hashes, nonce words or lease ownership do not independently grant authority.
internal static partial class WarpPortableSourceFaultServices
{
    public static uint TakePreparedFault(uint[] state, uint[] arena,
        uint controller, uint worker, uint run, uint dispatch, uint collectionEpoch,
        uint preparedIndex, uint ticketGeneration, uint nonce0, uint nonce1, uint nonce2, uint nonce3,
        uint sourceFunction, uint sourceOffset, uint sourceOpcode, uint effectIndex, uint factoryRow, uint faultDescriptor,
        uint sourcePhysical, uint sourceActivation, uint sourceBodyFunction, uint ownerPhysical, uint ownerActivation, uint rootRevision)
    {
        uint fault = ValidateTicket(state, arena, controller, worker, run, dispatch, collectionEpoch,
            preparedIndex, ticketGeneration, nonce0, nonce1, nonce2, nonce3, sourceFunction, sourceOffset, sourceOpcode, effectIndex,
            factoryRow, faultDescriptor, sourcePhysical, sourceActivation, sourceBodyFunction, ownerPhysical, ownerActivation, rootRevision, 0);
        if (fault != 0) { return fault; }
        uint record = Prepared(arena, arena[WarpPortableSourceFaultFactoryLayout.Descriptor], preparedIndex);
        arena[record + WarpPortableSourceFaultFactoryLayout.PreparedState] = WarpPortableSourceFaultFactoryLayout.Taken;
        return 0;
    }

    public static uint RaiseTakenPreparedFault(uint[] state, uint[] arena,
        uint controller, uint worker, uint run, uint dispatch, uint collectionEpoch,
        uint preparedIndex, uint ticketGeneration, uint nonce0, uint nonce1, uint nonce2, uint nonce3,
        uint sourceFunction, uint sourceOffset, uint sourceOpcode, uint effectIndex, uint factoryRow, uint faultDescriptor,
        uint sourcePhysical, uint sourceActivation, uint sourceBodyFunction, uint ownerPhysical, uint ownerActivation, uint rootRevision)
    {
        uint fault = ValidateTicket(state, arena, controller, worker, run, dispatch, collectionEpoch,
            preparedIndex, ticketGeneration, nonce0, nonce1, nonce2, nonce3, sourceFunction, sourceOffset, sourceOpcode, effectIndex,
            factoryRow, faultDescriptor, sourcePhysical, sourceActivation, sourceBodyFunction, ownerPhysical, ownerActivation, rootRevision, 1);
        if (fault != 0) { return fault; }
        uint pool = arena[WarpPortableSourceFaultFactoryLayout.Descriptor]; uint record = Prepared(arena, pool, preparedIndex);
        uint row = Factory(arena, pool, factoryRow); uint reference = record + WarpPortableSourceFaultFactoryLayout.PreparedReference;
        fault = WarpPortableExceptionServices.RaiseFaultOperation(arena, controller, worker, run, dispatch,
            arena[record + WarpPortableSourceFaultFactoryLayout.PreparedExceptionRecord], sourceBodyFunction, sourceOffset, sourceOpcode, effectIndex,
            arena[reference], arena[reference + 1], arena[reference + 2], arena[row + WarpPortableSourceFaultFactoryLayout.RowExceptionType], faultDescriptor);
        if (fault != 0) { return fault; }
        uint descriptor = arena[WarpPortableExceptionLayout.Descriptor];
        uint raised = ExceptionRecord(arena, descriptor, worker, arena[record + WarpPortableSourceFaultFactoryLayout.PreparedExceptionRecord]);
        arena[record + WarpPortableSourceFaultFactoryLayout.PreparedRaiseGeneration] = arena[raised + WarpPortableExceptionLayout.RaiseGeneration];
        return 0;
    }

    public static uint AcknowledgeRaisedFault(uint[] state, uint[] arena,
        uint controller, uint worker, uint run, uint dispatch, uint collectionEpoch,
        uint preparedIndex, uint ticketGeneration, uint nonce0, uint nonce1, uint nonce2, uint nonce3,
        uint sourceFunction, uint sourceOffset, uint sourceOpcode, uint effectIndex, uint factoryRow, uint faultDescriptor,
        uint sourcePhysical, uint sourceActivation, uint sourceBodyFunction, uint ownerPhysical, uint ownerActivation, uint rootRevision, uint raiseGeneration)
    {
        uint fault = ValidateTicket(state, arena, controller, worker, run, dispatch, collectionEpoch,
            preparedIndex, ticketGeneration, nonce0, nonce1, nonce2, nonce3, sourceFunction, sourceOffset, sourceOpcode, effectIndex,
            factoryRow, faultDescriptor, sourcePhysical, sourceActivation, sourceBodyFunction, ownerPhysical, ownerActivation, rootRevision, 2);
        if (fault != 0) { return fault; }
        uint pool = arena[WarpPortableSourceFaultFactoryLayout.Descriptor]; uint record = Prepared(arena, pool, preparedIndex);
        if (raiseGeneration == 0 || arena[record + WarpPortableSourceFaultFactoryLayout.PreparedRaiseGeneration] != raiseGeneration)
        { return WarpPortableExceptionLayout.InvalidTicket; }
        uint root = Root(arena, arena[record + WarpPortableSourceFaultFactoryLayout.PreparedRoot]);
        if (arena[root + WarpPortableHeapLayout.RootGeneration] == uint.MaxValue || arena[WarpPortableHeapLayout.LiveRoots] == 0)
        { return WarpPortableExceptionLayout.GenerationExhausted; }
        // The complete EH publication was validated before touching this root.
        // The operation lease covers the triple clear and the final state store.
        arena[root + WarpPortableHeapLayout.RootReference] = 0;
        arena[root + WarpPortableHeapLayout.RootReference + 1] = 0;
        arena[root + WarpPortableHeapLayout.RootReference + 2] = 0;
        arena[root + WarpPortableHeapLayout.RootKind] = 0;
        arena[root + WarpPortableHeapLayout.RootGeneration]++;
        arena[root + WarpPortableHeapLayout.RootState] = WarpPortableHeapLayout.Retired;
        arena[WarpPortableHeapLayout.LiveRoots]--;
        arena[record + WarpPortableSourceFaultFactoryLayout.PreparedState] = WarpPortableSourceFaultFactoryLayout.Transferred;
        return 0;
    }
}
