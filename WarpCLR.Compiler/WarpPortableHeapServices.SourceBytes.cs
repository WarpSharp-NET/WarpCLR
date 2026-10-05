namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    public static uint ReadSourceByte(uint[] arena, uint context, uint slot, uint generation,
        uint byteOffset, uint byteSpan, uint elementType, uint relativeByte, uint readOnly)
    {
        if (Begin(arena, 41) != 0 || ValidateSourceByteOwner(arena, context, slot, generation, byteOffset, byteSpan, elementType, readOnly) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (relativeByte >= byteSpan) { return Fail(arena, WarpPortableHeapLayout.Bounds, relativeByte, byteSpan); }
        if (arena[Type(arena, elementType) + WarpPortableHeapLayout.TypeKind] != WarpPortableHeapLayout.Value ||
            arena[Type(arena, elementType) + WarpPortableHeapLayout.ReferenceCount] != 0)
        {
            if (arena[WarpPortableHeapLayout.LeaseState] == 0) { return Fail(arena, WarpPortableHeapLayout.Busy, slot, elementType); }
        }
        uint offset = byteOffset + relativeByte;
        uint payload = slot == 0 ? arena[Type(arena, generation) + WarpPortableHeapLayout.StaticStart] :
            arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotPayload];
        arena[WarpPortableHeapLayout.Result] = (arena[payload + (offset >> 2)] >> (int)((offset & 3u) * 8)) & 255u;
        return 0;
    }

    public static uint WriteSourceByte(uint[] arena, uint context, uint slot, uint generation,
        uint byteOffset, uint byteSpan, uint elementType, uint relativeByte, uint byteValue)
    {
        if (Begin(arena, 42) != 0 || ValidateSourceByteOwner(arena, context, slot, generation, byteOffset, byteSpan, elementType, 0) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (relativeByte >= byteSpan) { return Fail(arena, WarpPortableHeapLayout.Bounds, relativeByte, byteSpan); }
        if (arena[Type(arena, elementType) + WarpPortableHeapLayout.TypeKind] != WarpPortableHeapLayout.Value ||
            arena[Type(arena, elementType) + WarpPortableHeapLayout.ReferenceCount] != 0)
        {
            if (arena[WarpPortableHeapLayout.LeaseState] == 0) { return Fail(arena, WarpPortableHeapLayout.Busy, slot, elementType); }
        }
        uint offset = byteOffset + relativeByte;
        uint payload = slot == 0 ? arena[Type(arena, generation) + WarpPortableHeapLayout.StaticStart] :
            arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotPayload];
        uint shift = (offset & 3u) * 8;
        uint address = payload + (offset >> 2);
        arena[address] = (arena[address] & ~(255u << (int)shift)) | ((byteValue & 255u) << (int)shift);
        return 0;
    }
}
