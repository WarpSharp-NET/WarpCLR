namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    // Bootstrap preparation only. The caller retains its admitted heap lease
    // throughout all helper quanta. This does not grant source Take authority.
    public static uint PrepareSourceFaultResource(uint[] arena, uint resourceId)
    {
        if (Begin(arena, 58) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint descriptor = SourceFaultPoolDescriptor(arena);
        if (descriptor == 0 || RequireSourceExceptionLease(arena) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint row = SourceFaultResourceRow(arena, descriptor, resourceId);
        if (row == 0 || RequireEmptyFaultResource(arena, descriptor, row) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint text = arena[row + WarpPortableSourceFaultFactoryLayout.ResourceText];
        uint length = arena[row + WarpPortableSourceFaultFactoryLayout.ResourceLength];
        for (uint index = 0; index < length; index++)
        {
            if (arena[text + index] > 0xFFFFu) { return Fail(arena, WarpPortableHeapLayout.InvalidOperation, resourceId, index); }
        }
        arena[row + WarpPortableSourceFaultFactoryLayout.ResourceState] = WarpPortableSourceFaultFactoryLayout.Preparing;
        uint type = arena[descriptor + WarpPortableSourceFaultFactoryLayout.StringType];
        if (Allocate(arena, type, WarpPortableHeapLayout.String, length) != 0)
        {
            arena[row + WarpPortableSourceFaultFactoryLayout.ResourceState] = WarpPortableSourceFaultFactoryLayout.Failed;
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint payload = arena[Slot(arena, arena[WarpPortableHeapLayout.Result + 1]) + WarpPortableHeapLayout.SlotPayload];
        for (uint index = 0; index < length; index++) { arena[payload + (index >> 1)] |= arena[text + index] << (int)((index & 1u) * 16); }
        StoreFaultPoolReference(arena, row + WarpPortableSourceFaultFactoryLayout.ResourceReference,
            arena[row + WarpPortableSourceFaultFactoryLayout.ResourceRoot]);
        AcknowledgeServiceResult(arena, arena[WarpPortableHeapLayout.LeaseOwner]);
        arena[row + WarpPortableSourceFaultFactoryLayout.ResourceState] = WarpPortableSourceFaultFactoryLayout.Ready;
        return 0;
    }

    public static uint PrepareSourceFaultRecord(uint[] arena, uint preparedIndex)
    {
        if (Begin(arena, 59) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint descriptor = SourceFaultPoolDescriptor(arena);
        if (descriptor == 0 || RequireSourceExceptionLease(arena) != 0 || RequireScratch(arena, 0, WarpPortableSourceExceptionLayout.InputWords) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint record = SourceFaultPreparedRow(arena, descriptor, preparedIndex);
        if (record == 0 || RequireEmptyFaultRecord(arena, descriptor, record) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint row = SourceFaultFactoryRow(arena, descriptor, arena[record + WarpPortableSourceFaultFactoryLayout.PreparedFactoryRow]);
        if (row == 0 || RequirePreparedFaultSite(arena, record, row) != 0 || RequireFaultResourcesReady(arena, descriptor, row) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        return ConstructPreparedFaultRecord(arena, descriptor, record, row);
    }

    // Validates data and returns a rooted reference for a future admitted EH
    // operation. No ticket is minted, no state is consumed and no root released.
    public static uint ValidatePreparedSourceFault(uint[] arena, uint factoryRow, uint preparedIndex)
    {
        if (Begin(arena, 60) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint descriptor = SourceFaultPoolDescriptor(arena);
        if (descriptor == 0 || RequireSourceExceptionLease(arena) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint record = SourceFaultPreparedRow(arena, descriptor, preparedIndex), row = SourceFaultFactoryRow(arena, descriptor, factoryRow);
        if (record == 0 || row == 0 || arena[record + WarpPortableSourceFaultFactoryLayout.PreparedFactoryRow] != factoryRow ||
            RequireReadyFaultRecord(arena, descriptor, record, row) != 0)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, factoryRow, preparedIndex);
        }
        uint reference = record + WarpPortableSourceFaultFactoryLayout.PreparedReference;
        return SetReferenceResult(arena, arena[reference], arena[reference + 1], arena[reference + 2]);
    }
}
