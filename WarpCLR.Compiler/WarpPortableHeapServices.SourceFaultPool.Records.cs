namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    private static uint SourceFaultFactoryRow(uint[] arena, uint descriptor, uint id)
    {
        if (id == 0 || id > arena[descriptor + WarpPortableSourceFaultFactoryLayout.RowCount])
        {
            Fail(arena, WarpPortableHeapLayout.Bounds, id, arena[descriptor + WarpPortableSourceFaultFactoryLayout.RowCount]); return 0;
        }
        uint row = arena[descriptor + WarpPortableSourceFaultFactoryLayout.RowStart] + (id - 1) * WarpPortableSourceFaultFactoryLayout.RowWords;
        uint type = arena[row + WarpPortableSourceFaultFactoryLayout.RowExceptionType], memory = arena[WarpPortableSourceMemoryLayout.Descriptor];
        if (arena[row] != id || arena[row + WarpPortableSourceFaultFactoryLayout.RowFunction] == 0 ||
            arena[row + WarpPortableSourceFaultFactoryLayout.RowOpcode] > 0xFFFFu || RequireType(arena, type) != 0 ||
            arena[Type(arena, type) + WarpPortableHeapLayout.TypeKind] != WarpPortableHeapLayout.Class ||
            arena[Type(arena, type) + WarpPortableHeapLayout.TypePayloadWords] < WarpPortableSourceExceptionLayout.PrefixWords ||
            arena[arena[memory + WarpPortableSourceMemoryLayout.ExceptionTypeStart] + (type - 1) * WarpPortableSourceExceptionLayout.ExceptionTypeWords] == 0)
        {
            Fail(arena, WarpPortableHeapLayout.InvalidType, id, type); return 0;
        }
        return row;
    }

    private static uint RequirePreparedFaultSite(uint[] arena, uint record, uint row) =>
        arena[record + WarpPortableSourceFaultFactoryLayout.PreparedFunction] == arena[row + WarpPortableSourceFaultFactoryLayout.RowFunction] &&
        arena[record + WarpPortableSourceFaultFactoryLayout.PreparedOffset] == arena[row + WarpPortableSourceFaultFactoryLayout.RowOffset] &&
        arena[record + WarpPortableSourceFaultFactoryLayout.PreparedOpcode] == arena[row + WarpPortableSourceFaultFactoryLayout.RowOpcode] &&
        arena[record + WarpPortableSourceFaultFactoryLayout.PreparedEffect] == arena[row + WarpPortableSourceFaultFactoryLayout.RowEffect] ? 0 :
            Fail(arena, WarpPortableHeapLayout.InvalidOperation, arena[row], record);

    private static uint ConstructPreparedFaultRecord(uint[] arena, uint descriptor, uint record, uint row)
    {
        arena[record] = WarpPortableSourceFaultFactoryLayout.Preparing;
        uint type = arena[row + WarpPortableSourceFaultFactoryLayout.RowExceptionType];
        if (Allocate(arena, type, WarpPortableHeapLayout.Class, 0) != 0)
        {
            arena[record] = WarpPortableSourceFaultFactoryLayout.Failed; return arena[WarpPortableHeapLayout.Fault];
        }
        uint reference = record + WarpPortableSourceFaultFactoryLayout.PreparedReference;
        StoreFaultPoolReference(arena, reference, arena[record + WarpPortableSourceFaultFactoryLayout.PreparedRoot]);
        AcknowledgeServiceResult(arena, arena[WarpPortableHeapLayout.LeaseOwner]);
        uint input = arena[WarpPortableHeapLayout.ScratchStart];
        for (uint word = 0; word < WarpPortableSourceExceptionLayout.InputWords; word++) { arena[input + word] = 0; }
        CopyFaultResource(arena, descriptor, arena[row + WarpPortableSourceFaultFactoryLayout.RowMessage], input);
        CopyFaultResource(arena, descriptor, arena[row + WarpPortableSourceFaultFactoryLayout.RowParamName], input + 6);
        uint status = InitializeSourceExceptionData(arena, arena[reference], arena[reference + 1], arena[reference + 2], 0);
        arena[record] = status == 0 ? WarpPortableSourceFaultFactoryLayout.Ready : WarpPortableSourceFaultFactoryLayout.Failed;
        return status;
    }

    private static uint RequireReadyFaultRecord(uint[] arena, uint descriptor, uint record, uint row)
    {
        uint reference = record + WarpPortableSourceFaultFactoryLayout.PreparedReference;
        uint context = arena[reference], slot = arena[reference + 1], generation = arena[reference + 2];
        if (arena[record] != WarpPortableSourceFaultFactoryLayout.Ready || RequirePreparedFaultSite(arena, record, row) != 0 ||
            SourceExceptionRecord(arena, context, slot, generation) == 0 || RequireSourceExceptionDataInitialized(arena, slot, generation) != 0 ||
            arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotType] != arena[row + WarpPortableSourceFaultFactoryLayout.RowExceptionType] ||
            RequireFaultPoolRoot(arena, descriptor, arena[record + WarpPortableSourceFaultFactoryLayout.PreparedRoot],
                arena[record + WarpPortableSourceFaultFactoryLayout.PreparedRootGeneration], context, slot, generation, 0) != 0 ||
            RequireFaultResourcesReady(arena, descriptor, row) != 0)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidReference, slot, generation);
        }
        for (uint word = WarpPortableSourceFaultFactoryLayout.PreparedTicketGeneration; word <= WarpPortableSourceFaultFactoryLayout.PreparedExceptionRootGeneration; word++)
        {
            if (arena[record + word] != 0) { return Fail(arena, WarpPortableHeapLayout.InvalidOperation, record, word); }
        }
        for (uint word = WarpPortableSourceFaultFactoryLayout.PreparedController; word < WarpPortableSourceFaultFactoryLayout.PreparedWords; word++)
        {
            if (arena[record + word] != 0) { return Fail(arena, WarpPortableHeapLayout.InvalidOperation, record, word); }
        }
        uint payload = arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotPayload];
        if (FaultResourceMatches(arena, descriptor, arena[row + WarpPortableSourceFaultFactoryLayout.RowMessage], payload) == 0 ||
            FaultResourceMatches(arena, descriptor, arena[row + WarpPortableSourceFaultFactoryLayout.RowParamName], payload + WarpPortableSourceExceptionLayout.ParamNameWord) == 0)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, slot, generation);
        }
        return RequireFiniteFaultData(arena, payload, row);
    }

    private static uint RequireFiniteFaultData(uint[] arena, uint payload, uint row)
    {
        uint memory = arena[WarpPortableSourceMemoryLayout.Descriptor];
        uint type = arena[row + WarpPortableSourceFaultFactoryLayout.RowExceptionType];
        uint record = arena[memory + WarpPortableSourceMemoryLayout.ExceptionTypeStart] + (type - 1) * WarpPortableSourceExceptionLayout.ExceptionTypeWords;
        if (arena[payload + WarpPortableSourceExceptionLayout.HResultWord] != arena[record + WarpPortableSourceExceptionLayout.ExceptionTypeHResult])
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, type, WarpPortableSourceExceptionLayout.HResultWord);
        }
        for (uint word = 3; word < WarpPortableSourceExceptionLayout.HResultWord; word++)
        {
            if (word >= WarpPortableSourceExceptionLayout.ParamNameWord && word < WarpPortableSourceExceptionLayout.ActualValueWord) { continue; }
            if (arena[payload + word] != 0) { return Fail(arena, WarpPortableHeapLayout.InvalidOperation, type, word); }
        }
        return 0;
    }
}
