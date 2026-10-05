namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    private static uint SourceExceptionRecord(uint[] arena, uint context, uint slot, uint generation)
    {
        if (RequireReference(arena, context, slot, generation, 0) != 0) { return 0; }
        uint descriptor = SourceViewDescriptor(arena);
        if (descriptor == 0) { Fail(arena, WarpPortableHeapLayout.InvalidType, slot, 0); return 0; }
        uint entry = Slot(arena, slot); uint id = arena[entry + WarpPortableHeapLayout.SlotType];
        if (RequireType(arena, id) != 0) { return 0; }
        uint record = arena[descriptor + WarpPortableSourceMemoryLayout.ExceptionTypeStart] + (id - 1) * WarpPortableSourceExceptionLayout.ExceptionTypeWords;
        uint kind = arena[record + WarpPortableSourceExceptionLayout.ExceptionTypeKind];
        uint payload = arena[entry + WarpPortableHeapLayout.SlotPayload]; uint words = arena[entry + WarpPortableHeapLayout.SlotPayloadWords];
        if (kind == 0 || kind > WarpPortableSourceExceptionKind.TypeInitialization || arena[record + WarpPortableSourceExceptionLayout.ExceptionTypeHResult] == 0 ||
            arena[entry + WarpPortableHeapLayout.SlotKind] != WarpPortableHeapLayout.Class || words < WarpPortableSourceExceptionLayout.PrefixWords ||
            payload < arena[WarpPortableHeapLayout.DataStart] || payload > (uint)arena.Length || words > (uint)arena.Length - payload)
        {
            Fail(arena, WarpPortableHeapLayout.InvalidType, id, kind); return 0;
        }
        return record;
    }

    private static uint SourceExceptionDataState(uint[] arena, uint slot)
    {
        uint descriptor = arena[WarpPortableSourceMemoryLayout.Descriptor];
        return arena[descriptor + WarpPortableSourceMemoryLayout.ExceptionTypeStart] +
            arena[WarpPortableHeapLayout.TypeCount] * WarpPortableSourceExceptionLayout.ExceptionTypeWords + (slot - 1) * WarpPortableSourceExceptionLayout.DataStateWords;
    }

    private static uint RequireSourceExceptionDataInitialized(uint[] arena, uint slot, uint generation)
    {
        uint state = SourceExceptionDataState(arena, slot);
        return arena[state + WarpPortableSourceExceptionLayout.DataGeneration] == generation &&
            arena[state + WarpPortableSourceExceptionLayout.DataInitialized] == 1 ? 0 :
            Fail(arena, WarpPortableHeapLayout.InvalidOperation, slot, generation);
    }

    private static uint RequireSourceExceptionLease(uint[] arena) => arena[WarpPortableHeapLayout.LeaseState] != 0 ? 0 :
        Fail(arena, WarpPortableHeapLayout.Busy, arena[WarpPortableHeapLayout.LeaseOwner], arena[WarpPortableHeapLayout.LeaseEpoch]);

    private static uint SourceExceptionInputField(uint index) => index < 2 ? index * 3 : (index + 2) * 3;

    private static uint SourceExceptionFieldAllowed(uint kind, uint field) =>
        field == WarpPortableSourceExceptionLayout.MessageWord || field == WarpPortableSourceExceptionLayout.InnerExceptionWord ||
        field == WarpPortableSourceExceptionLayout.HResultWord ||
        field == WarpPortableSourceExceptionLayout.ParamNameWord && SourceExceptionArgumentKind(kind) != 0 ||
        field == WarpPortableSourceExceptionLayout.ActualValueWord && kind == WarpPortableSourceExceptionKind.ArgumentOutOfRange ||
        field == WarpPortableSourceExceptionLayout.TypeNameWord && kind == WarpPortableSourceExceptionKind.TypeInitialization ? 1u : 0u;

    private static uint SourceExceptionArgumentKind(uint kind) => kind == WarpPortableSourceExceptionKind.Argument ||
        kind == WarpPortableSourceExceptionKind.ArgumentOutOfRange || kind == WarpPortableSourceExceptionKind.ArgumentNull ? 1u : 0u;
}
