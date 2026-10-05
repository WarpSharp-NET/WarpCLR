namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    // This prepares data once for one allocated generation. It is not a pool ticket,
    // a constructor dispatcher, or authority to raise an implicit source exception.
    public static uint InitializeSourceExceptionData(uint[] arena, uint context, uint slot, uint generation, uint scratchOffset)
    {
        if (Begin(arena, 55) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint record = SourceExceptionRecord(arena, context, slot, generation);
        if (record == 0 || RequireSourceExceptionLease(arena) != 0 ||
            RequireScratch(arena, scratchOffset, WarpPortableSourceExceptionLayout.InputWords) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint entry = Slot(arena, slot); uint payload = arena[entry + WarpPortableHeapLayout.SlotPayload];
        uint state = SourceExceptionDataState(arena, slot);
        if (arena[state + WarpPortableSourceExceptionLayout.DataGeneration] == generation && arena[state + WarpPortableSourceExceptionLayout.DataInitialized] != 0)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, slot, generation);
        }
        for (uint word = 0; word < WarpPortableSourceExceptionLayout.PrefixWords; word++)
        {
            if (arena[payload + word] != 0) { return Fail(arena, WarpPortableHeapLayout.InvalidOperation, slot, word); }
        }
        uint input = arena[WarpPortableHeapLayout.ScratchStart] + scratchOffset;
        if (ValidateSourceExceptionData(arena, entry, record, input) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        for (uint field = 0; field < 5; field++)
        {
            uint output = payload + SourceExceptionInputField(field);
            for (uint word = 0; word < 3; word++) { arena[output + word] = arena[input + field * 3 + word]; }
        }
        arena[payload + WarpPortableSourceExceptionLayout.HResultWord] = arena[record + WarpPortableSourceExceptionLayout.ExceptionTypeHResult];
        arena[state + WarpPortableSourceExceptionLayout.DataGeneration] = generation;
        arena[state + WarpPortableSourceExceptionLayout.DataInitialized] = 1;
        return 0;
    }

    // Raw base-message data is distinct from the virtual CLR Message getter.
    // Argument/ActualValue formatting and null-message fallback must be generated separately.
    public static uint ReadSourceExceptionDataField(uint[] arena, uint context, uint slot, uint generation, uint field)
    {
        if (Begin(arena, 56) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint record = SourceExceptionRecord(arena, context, slot, generation);
        if (record == 0 || RequireSourceExceptionLease(arena) != 0 || RequireSourceExceptionDataInitialized(arena, slot, generation) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint kind = arena[record + WarpPortableSourceExceptionLayout.ExceptionTypeKind];
        if (SourceExceptionFieldAllowed(kind, field) == 0) { return Fail(arena, WarpPortableHeapLayout.InvalidOperation, field, kind); }
        uint payload = arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotPayload];
        if (field == WarpPortableSourceExceptionLayout.HResultWord)
        {
            arena[WarpPortableHeapLayout.Result] = arena[payload + field]; return 0;
        }
        return SetReferenceResult(arena, arena[payload + field], arena[payload + field + 1], arena[payload + field + 2]);
    }

    public static uint WriteSourceExceptionHResult(uint[] arena, uint context, uint slot, uint generation, uint value)
    {
        if (Begin(arena, 57) != 0 || SourceExceptionRecord(arena, context, slot, generation) == 0 || RequireSourceExceptionLease(arena) != 0 ||
            RequireSourceExceptionDataInitialized(arena, slot, generation) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint payload = arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotPayload];
        arena[payload + WarpPortableSourceExceptionLayout.HResultWord] = value;
        return 0;
    }
}
