namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    private static uint InitializerFailureText(uint[] arena, uint descriptor, uint record, uint message)
    {
        uint offset = message != 0 ? WarpPortableSourceInitializerFailureLayout.MessageText : WarpPortableSourceInitializerFailureLayout.NameText;
        uint start = arena[record + offset], length = arena[record + offset + 1];
        uint text = arena[descriptor + WarpPortableSourceInitializerFailureLayout.TextStart], units = arena[descriptor + WarpPortableSourceInitializerFailureLayout.TextUnits];
        if (start < text || start - text > units || length > units - (start - text))
        {
            Fail(arena, WarpPortableHeapLayout.Bounds, start, length); return 0;
        }
        for (uint index = 0; index < length; index++)
        {
            if (arena[start + index] > 0xFFFFu) { Fail(arena, WarpPortableHeapLayout.InvalidOperation, start, index); return 0; }
        }
        return start;
    }

    private static uint PrepareInitializerFailureString(uint[] arena, uint descriptor, uint record, uint message)
    {
        uint text = InitializerFailureText(arena, descriptor, record, message);
        if (text == 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint length = arena[record + (message != 0 ? WarpPortableSourceInitializerFailureLayout.MessageUnits : WarpPortableSourceInitializerFailureLayout.NameUnits)];
        uint stringType = arena[descriptor + WarpPortableSourceInitializerFailureLayout.StringType];
        if (RequireType(arena, stringType) != 0 || arena[Type(arena, stringType) + WarpPortableHeapLayout.TypeKind] != WarpPortableHeapLayout.String)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidType, stringType, 0);
        }
        if (Allocate(arena, stringType, WarpPortableHeapLayout.String, length) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint payload = arena[Slot(arena, arena[WarpPortableHeapLayout.Result + 1]) + WarpPortableHeapLayout.SlotPayload];
        for (uint index = 0; index < length; index++) { arena[payload + (index >> 1)] |= arena[text + index] << (int)((index & 1u) * 16); }
        StoreFaultPoolReference(arena, record + (message != 0 ? WarpPortableSourceInitializerFailureLayout.MessageReference : WarpPortableSourceInitializerFailureLayout.NameReference),
            arena[record + WarpPortableSourceInitializerFailureLayout.FirstRoot] + message);
        return AcknowledgeServiceResult(arena, arena[WarpPortableHeapLayout.LeaseOwner]);
    }

    private static uint RequireInitializerFailureString(uint[] arena, uint descriptor, uint record, uint message)
    {
        uint text = InitializerFailureText(arena, descriptor, record, message);
        if (text == 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint reference = record + (message != 0 ? WarpPortableSourceInitializerFailureLayout.MessageReference : WarpPortableSourceInitializerFailureLayout.NameReference);
        uint context = arena[reference], slot = arena[reference + 1], generation = arena[reference + 2];
        if (RequireReference(arena, context, slot, generation, 0) != 0 ||
            RequireFaultPoolRoot(arena, descriptor, arena[record + WarpPortableSourceInitializerFailureLayout.FirstRoot] + message,
                arena[record + WarpPortableSourceInitializerFailureLayout.RootGeneration], context, slot, generation, 0) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint length = arena[record + (message != 0 ? WarpPortableSourceInitializerFailureLayout.MessageUnits : WarpPortableSourceInitializerFailureLayout.NameUnits)];
        uint entry = Slot(arena, slot), payload = arena[entry + WarpPortableHeapLayout.SlotPayload];
        if (arena[entry + WarpPortableHeapLayout.SlotType] != arena[descriptor + WarpPortableSourceInitializerFailureLayout.StringType] ||
            arena[entry + WarpPortableHeapLayout.SlotKind] != WarpPortableHeapLayout.String || arena[entry + WarpPortableHeapLayout.SlotLength] != length ||
            payload < arena[WarpPortableHeapLayout.DataStart] || payload > (uint)arena.Length || ((length >> 1) + (length & 1u)) > (uint)arena.Length - payload)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidType, slot, length);
        }
        for (uint index = 0; index < length; index++)
        {
            if (((arena[payload + (index >> 1)] >> (int)((index & 1u) * 16)) & 0xFFFFu) != arena[text + index])
            {
                return Fail(arena, WarpPortableHeapLayout.InvalidOperation, slot, index);
            }
        }
        return 0;
    }
}
