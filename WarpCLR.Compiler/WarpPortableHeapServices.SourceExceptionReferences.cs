namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    private static uint ValidateSourceExceptionData(uint[] arena, uint entry, uint record, uint input)
    {
        uint kind = arena[record + WarpPortableSourceExceptionLayout.ExceptionTypeKind];
        uint type = Type(arena, arena[entry + WarpPortableHeapLayout.SlotType]);
        for (uint index = 0; index < 5; index++)
        {
            uint field = SourceExceptionInputField(index); uint value = input + index * 3;
            uint context = arena[value]; uint slot = arena[value + 1]; uint generation = arena[value + 2];
            if (SourceExceptionFieldAllowed(kind, field) == 0 && (context | slot | generation) != 0)
            {
                return Fail(arena, WarpPortableHeapLayout.InvalidOperation, field, kind);
            }
            uint expected = SourceExceptionRootType(arena, type, field);
            if (expected == 0 || RequireType(arena, expected) != 0) { return Fail(arena, WarpPortableHeapLayout.InvalidType, field, expected); }
            if (RequireReference(arena, context, slot, generation, 1) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
            if (slot != 0 && Assignable(arena, arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotType], expected) == 0)
            {
                return Fail(arena, WarpPortableHeapLayout.InvalidCast, field, expected);
            }
        }
        return 0;
    }

    private static uint SourceExceptionRootType(uint[] arena, uint type, uint field)
    {
        uint count = arena[type + WarpPortableHeapLayout.ReferenceCount];
        uint start = arena[type + WarpPortableHeapLayout.ReferenceMap]; uint end = arena[WarpPortableHeapLayout.DataStart];
        if (count > 0x10000u || start < WarpPortableHeapLayout.HeaderWords || start > end || count * 2 > end - start) { return 0; }
        for (uint index = 0; index < count; index++)
        {
            uint row = start + index * 2;
            if (arena[row] == field) { return arena[row + 1]; }
        }
        return 0;
    }
}
