namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    internal const string SourceNullableSemantics = "warp.nullable/underlying-box-null-default-owned-unbox-copy/0.1";

    public static uint MaterializeSourceNullable(uint[] arena, uint context, uint slot, uint generation, uint nullableType)
    {
        if (Begin(arena, 43) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint shape = SourceNullableShape(arena, nullableType);
        if (shape == 0 || RequireSourceNullableInput(arena, context, slot, generation, shape) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        if (arena[WarpPortableHeapLayout.LeaseState] == 0) { return Fail(arena, WarpPortableHeapLayout.Busy, slot, nullableType); }
        if (Allocate(arena, nullableType, WarpPortableHeapLayout.Box, 0) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint output = arena[Slot(arena, arena[WarpPortableHeapLayout.Result + 1]) + WarpPortableHeapLayout.SlotPayload];
        if (slot != 0)
        {
            uint descriptor = arena[WarpPortableSourceMemoryLayout.Descriptor];
            uint bytes = arena[SourceMemoryType(arena, descriptor, arena[shape + WarpPortableSourceMemoryLayout.NullableElementType])];
            uint input = arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotPayload];
            SourceCopyBytes(arena, input, 0, output, arena[shape + WarpPortableSourceMemoryLayout.NullableValueByte], bytes);
            SourceWriteByte(arena, output, arena[shape + WarpPortableSourceMemoryLayout.NullableHasValueByte], 1);
        }
        return 0;
    }

    public static uint UnboxSourceNullableValue(uint[] arena, uint context, uint slot, uint generation, uint nullableType, uint scratchOffset)
    {
        if (Begin(arena, 44) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint shape = SourceNullableShape(arena, nullableType);
        if (shape == 0 || RequireSourceNullableInput(arena, context, slot, generation, shape) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint words = arena[Type(arena, nullableType) + WarpPortableHeapLayout.TypePayloadWords];
        if (RequireScratch(arena, scratchOffset, words) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        if (arena[WarpPortableHeapLayout.LeaseState] == 0) { return Fail(arena, WarpPortableHeapLayout.Busy, slot, nullableType); }
        uint output = arena[WarpPortableHeapLayout.ScratchStart] + scratchOffset;
        for (uint word = 0; word < words; word++) { arena[output + word] = 0; }
        if (slot != 0)
        {
            uint descriptor = arena[WarpPortableSourceMemoryLayout.Descriptor];
            uint bytes = arena[SourceMemoryType(arena, descriptor, arena[shape + WarpPortableSourceMemoryLayout.NullableElementType])];
            uint input = arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotPayload];
            SourceCopyBytes(arena, input, 0, output, arena[shape + WarpPortableSourceMemoryLayout.NullableValueByte], bytes);
            SourceWriteByte(arena, output, arena[shape + WarpPortableSourceMemoryLayout.NullableHasValueByte], 1);
        }
        return 0;
    }

    public static uint BoxSourceNullableValue(uint[] arena, uint nullableType, uint scratchOffset)
    {
        if (Begin(arena, 45) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint shape = SourceNullableShape(arena, nullableType);
        if (shape == 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint nullable = Type(arena, nullableType);
        if (RequireScratch(arena, scratchOffset, arena[nullable + WarpPortableHeapLayout.TypePayloadWords]) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        if (arena[WarpPortableHeapLayout.LeaseState] == 0) { return Fail(arena, WarpPortableHeapLayout.Busy, scratchOffset, nullableType); }
        uint input = arena[WarpPortableHeapLayout.ScratchStart] + scratchOffset;
        if (SourceReadByte(arena, input, arena[shape + WarpPortableSourceMemoryLayout.NullableHasValueByte]) == 0) { return 0; }
        if (ValidateValueReferences(arena, nullable, scratchOffset) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint element = arena[shape + WarpPortableSourceMemoryLayout.NullableElementType];
        if (Allocate(arena, element, WarpPortableHeapLayout.Box, 0) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        uint descriptor = arena[WarpPortableSourceMemoryLayout.Descriptor];
        uint bytes = arena[SourceMemoryType(arena, descriptor, element)];
        uint output = arena[Slot(arena, arena[WarpPortableHeapLayout.Result + 1]) + WarpPortableHeapLayout.SlotPayload];
        SourceCopyBytes(arena, input, arena[shape + WarpPortableSourceMemoryLayout.NullableValueByte], output, 0, bytes);
        return 0;
    }

    private static uint SourceNullableShape(uint[] arena, uint nullableType)
    {
        uint descriptor = SourceViewDescriptor(arena);
        if (descriptor == 0 || RequireType(arena, nullableType) != 0)
        {
            Fail(arena, WarpPortableHeapLayout.InvalidType, nullableType, descriptor); return 0;
        }
        uint start = arena[descriptor + WarpPortableSourceMemoryLayout.NullableStart];
        uint count = arena[descriptor + WarpPortableSourceMemoryLayout.NullableCount];
        for (uint index = 0; index < count; index++)
        {
            uint row = start + index * WarpPortableSourceMemoryLayout.NullableWords;
            if (arena[row] != nullableType) { continue; }
            uint element = arena[row + WarpPortableSourceMemoryLayout.NullableElementType];
            if (RequireType(arena, element) != 0 || arena[Type(arena, nullableType) + WarpPortableHeapLayout.TypeKind] != WarpPortableHeapLayout.Value ||
                arena[Type(arena, element) + WarpPortableHeapLayout.TypeKind] != WarpPortableHeapLayout.Value) { break; }
            uint total = arena[SourceMemoryType(arena, descriptor, nullableType)];
            uint bytes = arena[SourceMemoryType(arena, descriptor, element)];
            uint hasValue = arena[row + WarpPortableSourceMemoryLayout.NullableHasValueByte];
            uint value = arena[row + WarpPortableSourceMemoryLayout.NullableValueByte];
            if (hasValue >= total || value > total || bytes > total - value || hasValue >= value && hasValue - value < bytes ||
                total > 0x20000u || bytes > 0x20000u || bytes == 0) { break; }
            return row;
        }
        Fail(arena, WarpPortableHeapLayout.InvalidType, nullableType, 0); return 0;
    }

    private static uint RequireSourceNullableInput(uint[] arena, uint context, uint slot, uint generation, uint shape)
    {
        if (RequireReference(arena, context, slot, generation, 1) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
        if (slot == 0) { return 0; }
        uint entry = Slot(arena, slot);
        uint element = arena[shape + WarpPortableSourceMemoryLayout.NullableElementType];
        return arena[entry + WarpPortableHeapLayout.SlotKind] == WarpPortableHeapLayout.Box && arena[entry + WarpPortableHeapLayout.SlotType] == element ? 0 :
            Fail(arena, WarpPortableHeapLayout.InvalidCast, slot, element);
    }

    private static uint SourceReadByte(uint[] arena, uint payload, uint offset) =>
        (arena[payload + (offset >> 2)] >> (int)((offset & 3u) * 8)) & 255u;

    private static uint SourceWriteByte(uint[] arena, uint payload, uint offset, uint value)
    {
        uint shift = (offset & 3u) * 8;
        uint word = payload + (offset >> 2);
        arena[word] = (arena[word] & ~(255u << (int)shift)) | ((value & 255u) << (int)shift); return 0;
    }

    private static uint SourceCopyBytes(uint[] arena, uint input, uint inputOffset, uint output, uint outputOffset, uint count)
    {
        for (uint index = 0; index < count; index++)
        {
            SourceWriteByte(arena, output, outputOffset + index, SourceReadByte(arena, input, inputOffset + index));
        }
        return 0;
    }
}
