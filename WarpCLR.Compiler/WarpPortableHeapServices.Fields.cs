namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    public static uint ReadWord(uint[] arena, uint context, uint slot, uint generation, uint offset)
    {
        if (Begin(arena, 6) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequirePayload(arena, context, slot, generation, offset, 1) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (ReferenceTypeAt(arena, slot, offset, 1) != 0)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, slot, offset);
        }
        arena[WarpPortableHeapLayout.Result] = arena[arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotPayload] + offset];
        return 0;
    }

    public static uint WriteWord(uint[] arena, uint context, uint slot, uint generation, uint offset, uint value)
    {
        if (Begin(arena, 7) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequirePayload(arena, context, slot, generation, offset, 1) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (ReferenceTypeAt(arena, slot, offset, 1) != 0 || arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotKind] == WarpPortableHeapLayout.String)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, slot, offset);
        }
        arena[arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotPayload] + offset] = value;
        return 0;
    }

    public static uint ReadReference(uint[] arena, uint context, uint slot, uint generation, uint offset)
    {
        if (Begin(arena, 8) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequirePayload(arena, context, slot, generation, offset, 3) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (ReferenceTypeAt(arena, slot, offset, 0) == 0)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, slot, offset);
        }
        uint payload = arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotPayload] + offset;
        SetReferenceResult(arena, arena[payload], arena[payload + 1], arena[payload + 2]);
        return 0;
    }

    public static uint WriteReference(uint[] arena, uint context, uint slot, uint generation, uint offset,
        uint valueContext, uint valueSlot, uint valueGeneration)
    {
        if (Begin(arena, 9) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequirePayload(arena, context, slot, generation, offset, 3) != 0 || RequireReference(arena, valueContext, valueSlot, valueGeneration, 1) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint targetType = ReferenceTypeAt(arena, slot, offset, 0);
        if (targetType == 0)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, slot, offset);
        }
        if (valueSlot != 0 && Assignable(arena, arena[Slot(arena, valueSlot) + WarpPortableHeapLayout.SlotType], targetType) == 0)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidCast, valueSlot, targetType);
        }
        uint payload = arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotPayload] + offset;
        arena[payload] = valueContext;
        arena[payload + 1] = valueSlot;
        arena[payload + 2] = valueGeneration;
        return 0;
    }

    public static uint ArrayElement(uint[] arena, uint context, uint slot, uint generation, uint index)
    {
        if (Begin(arena, 10) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (RequireReference(arena, context, slot, generation, 0) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint entry = Slot(arena, slot);
        uint kind = arena[entry + WarpPortableHeapLayout.SlotKind];
        if (kind != WarpPortableHeapLayout.ReferenceArray && kind != WarpPortableHeapLayout.ValueArray)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, slot, kind);
        }
        if (index >= arena[entry + WarpPortableHeapLayout.SlotLength])
        {
            return Fail(arena, WarpPortableHeapLayout.Bounds, index, arena[entry + WarpPortableHeapLayout.SlotLength]);
        }
        uint type = Type(arena, arena[entry + WarpPortableHeapLayout.SlotType]);
        arena[WarpPortableHeapLayout.Result] = index * arena[type + WarpPortableHeapLayout.ElementWords];
        arena[WarpPortableHeapLayout.Result + 1] = arena[type + WarpPortableHeapLayout.ElementWords];
        arena[WarpPortableHeapLayout.Result + 2] = arena[type + WarpPortableHeapLayout.ElementType];
        return 0;
    }

    public static uint GetLength(uint[] arena, uint context, uint slot, uint generation)
    {
        if (Begin(arena, 35) != 0 || RequireReference(arena, context, slot, generation, 0) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint entry = Slot(arena, slot);
        uint kind = arena[entry + WarpPortableHeapLayout.SlotKind];
        if (kind != WarpPortableHeapLayout.ReferenceArray && kind != WarpPortableHeapLayout.ValueArray && kind != WarpPortableHeapLayout.String)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, slot, kind);
        }
        arena[WarpPortableHeapLayout.Result] = arena[entry + WarpPortableHeapLayout.SlotLength];
        return 0;
    }

    private static uint RequirePayload(uint[] arena, uint context, uint slot, uint generation, uint offset, uint words)
    {
        if (RequireReference(arena, context, slot, generation, 0) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        uint length = arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotPayloadWords];
        return offset <= length && words <= length - offset ? 0 : Fail(arena, WarpPortableHeapLayout.Bounds, offset, words);
    }

    private static uint ReferenceTypeAt(uint[] arena, uint slot, uint offset, uint includeInterior)
    {
        uint entry = Slot(arena, slot);
        uint kind = arena[entry + WarpPortableHeapLayout.SlotKind];
        uint type = Type(arena, arena[entry + WarpPortableHeapLayout.SlotType]);
        if (kind == WarpPortableHeapLayout.ReferenceArray)
        {
            return includeInterior != 0 || WarpPortableInteger32.RemainderUnsigned(offset, 3) == 0 ? arena[type + WarpPortableHeapLayout.ElementType] : 0;
        }
        if (kind == WarpPortableHeapLayout.ValueArray)
        {
            offset = WarpPortableInteger32.RemainderUnsigned(offset, arena[type + WarpPortableHeapLayout.ElementWords]);
            type = Type(arena, arena[type + WarpPortableHeapLayout.ElementType]);
        }
        uint map = arena[type + WarpPortableHeapLayout.ReferenceMap];
        uint count = arena[type + WarpPortableHeapLayout.ReferenceCount];
        for (uint index = 0; index < count; index++)
        {
            uint start = arena[map + index * 2];
            if (offset == start || (includeInterior != 0 && offset > start && offset - start < 3))
            {
                return arena[map + index * 2 + 1];
            }
        }
        return 0;
    }
}
