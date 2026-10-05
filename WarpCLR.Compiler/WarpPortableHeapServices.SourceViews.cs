namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    public static uint ValidateSourceByteOwner(uint[] arena, uint context, uint slot, uint generation,
        uint byteOffset, uint byteSpan, uint elementType, uint readOnly)
    {
        if ((context | slot | generation) == 0) { return Fail(arena, WarpPortableHeapLayout.NullReference, 0, 0); }
        if (context != arena[WarpPortableHeapLayout.Context]) { return Fail(arena, WarpPortableHeapLayout.WrongContext, context, arena[WarpPortableHeapLayout.Context]); }
        uint descriptor = SourceViewDescriptor(arena);
        if (descriptor == 0 || RequireType(arena, elementType) != 0 || readOnly > 1) { return Fail(arena, WarpPortableHeapLayout.InvalidType, elementType, descriptor); }
        uint ownerType;
        uint ownerKind;
        uint words;
        uint viewOffset = byteOffset;
        if (slot == 0 && generation != 0)
        {
            if (RequireType(arena, generation) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
            ownerType = generation; ownerKind = WarpPortableSourceMemoryLayout.Static;
            words = arena[Type(arena, ownerType) + WarpPortableHeapLayout.StaticWords];
        }
        else
        {
            if (RequireReference(arena, context, slot, generation, 0) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
            uint entry = Slot(arena, slot); ownerType = arena[entry + WarpPortableHeapLayout.SlotType];
            if (RequireType(arena, ownerType) != 0) { return arena[WarpPortableHeapLayout.Fault]; }
            words = arena[entry + WarpPortableHeapLayout.SlotPayloadWords];
            ownerKind = WarpPortableSourceMemoryLayout.Instance;
            uint kind = arena[entry + WarpPortableHeapLayout.SlotKind];
            if (kind == WarpPortableHeapLayout.ReferenceArray || kind == WarpPortableHeapLayout.ValueArray)
            {
                ownerKind = WarpPortableSourceMemoryLayout.ArrayElement;
                uint stride = arena[SourceMemoryType(arena, descriptor, ownerType) + WarpPortableSourceMemoryLayout.StrideBytes];
                if (stride == 0 || WarpPortableInteger32.DivideUnsigned(byteOffset, stride) >= arena[entry + WarpPortableHeapLayout.SlotLength])
                {
                    return Fail(arena, WarpPortableHeapLayout.Bounds, byteOffset, byteSpan);
                }
                viewOffset = WarpPortableInteger32.RemainderUnsigned(byteOffset, stride);
            }
        }
        uint expected = arena[SourceMemoryType(arena, descriptor, elementType) + WarpPortableSourceMemoryLayout.MemoryBytes];
        if (byteSpan == 0 || byteSpan != expected || byteSpan > 0x20000u ||
            (byteOffset >> 2) > words || ((byteOffset & 3u) + byteSpan + 3u) >> 2 > words - (byteOffset >> 2))
        {
            return Fail(arena, WarpPortableHeapLayout.Bounds, byteOffset, byteSpan);
        }
        return SourceViewMatches(arena, descriptor, ownerType, ownerKind, viewOffset, byteSpan, elementType, readOnly) != 0 ? 0 :
            Fail(arena, WarpPortableHeapLayout.InvalidCast, ownerType, elementType);
    }

    private static uint SourceMemoryType(uint[] arena, uint descriptor, uint type) =>
        arena[descriptor + WarpPortableSourceMemoryLayout.TypeStart] + (type - 1) * WarpPortableSourceMemoryLayout.TypeWords;

    private static uint SourceViewDescriptor(uint[] arena)
    {
        uint descriptor = arena[WarpPortableSourceMemoryLayout.Descriptor];
        uint end = arena[WarpPortableHeapLayout.DataStart];
        if (end > (uint)arena.Length || descriptor < WarpPortableHeapLayout.HeaderWords || descriptor > end ||
            WarpPortableSourceMemoryLayout.HeaderWords > end - descriptor || arena[descriptor] != WarpPortableSourceMemoryLayout.Magic ||
            arena[descriptor + 1] != WarpPortableSourceMemoryLayout.Version ||
            arena[descriptor + WarpPortableSourceMemoryLayout.TypeCount] != arena[WarpPortableHeapLayout.TypeCount]) { return 0; }
        uint typeCount = arena[descriptor + WarpPortableSourceMemoryLayout.TypeCount];
        uint typeStart = arena[descriptor + WarpPortableSourceMemoryLayout.TypeStart];
        uint viewCount = arena[descriptor + WarpPortableSourceMemoryLayout.ViewCount];
        uint viewStart = arena[descriptor + WarpPortableSourceMemoryLayout.ViewStart];
        uint nullableCount = arena[descriptor + WarpPortableSourceMemoryLayout.NullableCount];
        uint nullableStart = arena[descriptor + WarpPortableSourceMemoryLayout.NullableStart];
        if (typeCount > 4096 || viewCount > 209715 || typeStart < descriptor + WarpPortableSourceMemoryLayout.HeaderWords ||
            typeStart > end || typeCount * WarpPortableSourceMemoryLayout.TypeWords > end - typeStart ||
            viewStart < typeStart + typeCount * WarpPortableSourceMemoryLayout.TypeWords || viewStart > end ||
            viewCount * WarpPortableSourceMemoryLayout.ViewWords > end - viewStart || nullableCount > typeCount ||
            nullableStart < viewStart + viewCount * WarpPortableSourceMemoryLayout.ViewWords || nullableStart > end ||
            nullableCount * WarpPortableSourceMemoryLayout.NullableWords > end - nullableStart) { return 0; }
        uint exceptionEnd = SourceExceptionTablesEnd(arena, descriptor, end, nullableStart + nullableCount * WarpPortableSourceMemoryLayout.NullableWords);
        if (exceptionEnd == 0 || SourceFrameTables(arena, descriptor, end, exceptionEnd) == 0) { return 0; }
        return SourceShapeTables(arena, descriptor, end) != 0 ? descriptor : 0;
    }

    private static uint SourceExceptionTablesEnd(uint[] arena, uint descriptor, uint end, uint minimum)
    {
        uint start = arena[descriptor + WarpPortableSourceMemoryLayout.ExceptionTypeStart];
        uint types = arena[WarpPortableHeapLayout.TypeCount]; uint slots = arena[WarpPortableHeapLayout.SlotCount];
        uint words = types * WarpPortableSourceExceptionLayout.ExceptionTypeWords + slots * WarpPortableSourceExceptionLayout.DataStateWords;
        return slots <= 15420 && start >= minimum && start <= end && words <= end - start ? start + words : 0;
    }

    private static uint SourceShapeTables(uint[] arena, uint descriptor, uint end)
    {
        uint start = arena[descriptor + WarpPortableSourceArrayLayout.ShapeStart];
        uint count = arena[descriptor + WarpPortableSourceArrayLayout.ShapeCount];
        uint minimum = arena[descriptor + WarpPortableSourceMemoryLayout.FrameViewStart] +
            arena[descriptor + WarpPortableSourceMemoryLayout.FrameViewCount] * WarpPortableSourceMemoryLayout.FrameViewWords;
        return count == arena[WarpPortableHeapLayout.SlotCount] && count <= 15420 &&
            arena[descriptor + WarpPortableSourceArrayLayout.ShapeStride] == WarpPortableSourceArrayLayout.ShapeWords &&
            start >= minimum && start <= end && count * WarpPortableSourceArrayLayout.ShapeWords <= end - start ? 1u : 0u;
    }

    private static uint SourceFrameTables(uint[] arena, uint descriptor, uint end, uint minimum)
    {
        uint frameCount = arena[descriptor + WarpPortableSourceMemoryLayout.FrameCount];
        uint frameStart = arena[descriptor + WarpPortableSourceMemoryLayout.FrameStart];
        uint viewCount = arena[descriptor + WarpPortableSourceMemoryLayout.FrameViewCount];
        uint viewStart = arena[descriptor + WarpPortableSourceMemoryLayout.FrameViewStart];
        return frameCount <= 256 && frameStart >= minimum && frameStart <= end &&
            frameCount * WarpPortableSourceMemoryLayout.FrameWords <= end - frameStart &&
            viewCount <= 349525 && viewStart >= frameStart + frameCount * WarpPortableSourceMemoryLayout.FrameWords && viewStart <= end &&
            viewCount * WarpPortableSourceMemoryLayout.FrameViewWords <= end - viewStart ? 1u : 0u;
    }

    private static uint SourceViewMatches(uint[] arena, uint descriptor, uint ownerType, uint ownerKind,
        uint byteOffset, uint byteSpan, uint elementType, uint readOnly)
    {
        uint start = arena[descriptor + WarpPortableSourceMemoryLayout.ViewStart];
        uint count = arena[descriptor + WarpPortableSourceMemoryLayout.ViewCount];
        for (uint index = 0; index < count; index++)
        {
            uint row = start + index * WarpPortableSourceMemoryLayout.ViewWords;
            if (arena[row] == ownerType && arena[row + 1] == ownerKind && arena[row + 2] == byteOffset && arena[row + 3] == byteSpan)
            {
                uint actual = arena[row + 4];
                if (actual == elementType) { return 1; }
                if (readOnly != 0 && ownerKind == WarpPortableSourceMemoryLayout.ArrayElement && byteSpan == 12 && actual != 0 && actual <= arena[WarpPortableHeapLayout.TypeCount] &&
                    arena[Type(arena, actual) + WarpPortableHeapLayout.TypeKind] != WarpPortableHeapLayout.Value &&
                    arena[Type(arena, elementType) + WarpPortableHeapLayout.TypeKind] != WarpPortableHeapLayout.Value && Assignable(arena, actual, elementType) != 0) { return 1; }
            }
        }
        return 0;
    }
}
