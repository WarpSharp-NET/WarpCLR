namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    public static uint ReadSourceValue(uint[] arena, uint context, uint slot, uint generation,
        uint byteOffset, uint byteSpan, uint elementType, uint scratchOffset, uint readOnly)
    {
        if (Begin(arena, 49) != 0 || ValidateSourceByteOwner(arena, context, slot, generation, byteOffset, byteSpan, elementType, readOnly) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint words = (byteSpan + 3) >> 2;
        if (RequireScratch(arena, scratchOffset, words) != 0 || RequireSourceValueLease(arena, elementType) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint input = SourceValuePayload(arena, slot, generation);
        uint output = arena[WarpPortableHeapLayout.ScratchStart] + scratchOffset;
        for (uint word = 0; word < words; word++) { arena[output + word] = 0; }
        SourceCopyBytes(arena, input, byteOffset, output, 0, byteSpan);
        return ValidateSourceValueReferences(arena, elementType, scratchOffset);
    }

    public static uint WriteSourceValue(uint[] arena, uint context, uint slot, uint generation,
        uint byteOffset, uint byteSpan, uint elementType, uint scratchOffset)
    {
        if (Begin(arena, 50) != 0 || ValidateSourceByteOwner(arena, context, slot, generation, byteOffset, byteSpan, elementType, 0) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequireScratch(arena, scratchOffset, (byteSpan + 3) >> 2) != 0 || RequireSourceValueLease(arena, elementType) != 0 ||
            ValidateSourceValueReferences(arena, elementType, scratchOffset) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint input = arena[WarpPortableHeapLayout.ScratchStart] + scratchOffset;
        SourceCopyBytes(arena, input, 0, SourceValuePayload(arena, slot, generation), byteOffset, byteSpan);
        return 0;
    }

    public static uint InitializeSourceValue(uint[] arena, uint context, uint slot, uint generation,
        uint byteOffset, uint byteSpan, uint elementType)
    {
        if (Begin(arena, 51) != 0 || ValidateSourceByteOwner(arena, context, slot, generation, byteOffset, byteSpan, elementType, 0) != 0 ||
            RequireSourceValueLease(arena, elementType) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint output = SourceValuePayload(arena, slot, generation);
        for (uint index = 0; index < byteSpan; index++) { SourceWriteByte(arena, output, byteOffset + index, 0); }
        return 0;
    }

    private static uint SourceValuePayload(uint[] arena, uint slot, uint generation) => slot == 0 ?
        arena[Type(arena, generation) + WarpPortableHeapLayout.StaticStart] : arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotPayload];

    private static uint RequireSourceValueLease(uint[] arena, uint typeId) =>
        arena[Type(arena, typeId) + WarpPortableHeapLayout.TypeKind] == WarpPortableHeapLayout.Value &&
        arena[Type(arena, typeId) + WarpPortableHeapLayout.ReferenceCount] == 0 || arena[WarpPortableHeapLayout.LeaseState] != 0 ? 0 :
            Fail(arena, WarpPortableHeapLayout.Busy, typeId, arena[WarpPortableHeapLayout.LeaseOwner]);

    private static uint ValidateSourceValueReferences(uint[] arena, uint typeId, uint scratchOffset)
    {
        uint type = Type(arena, typeId);
        if (arena[type + WarpPortableHeapLayout.TypeKind] == WarpPortableHeapLayout.Value) { return ValidateValueReferences(arena, type, scratchOffset); }
        uint input = arena[WarpPortableHeapLayout.ScratchStart] + scratchOffset;
        if (RequireReference(arena, arena[input], arena[input + 1], arena[input + 2], 1) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint slot = arena[input + 1];
        return slot == 0 || Assignable(arena, arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotType], typeId) != 0 ? 0 :
            Fail(arena, WarpPortableHeapLayout.InvalidCast, slot, typeId);
    }
}
