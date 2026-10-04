namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    private static uint RewriteObjectReferences(uint[] source, uint[] destination, uint mappingIndex)
    {
        uint mapping = destination[WarpPortableHeapLayout.TransferStart] + mappingIndex * WarpPortableHeapLayout.TransferWords;
        uint sourceSlot = destination[mapping];
        uint targetSlot = destination[mapping + 2];
        uint entry = Slot(source, sourceSlot);
        uint sourcePayload = source[entry + WarpPortableHeapLayout.SlotPayload];
        uint targetPayload = destination[Slot(destination, targetSlot) + WarpPortableHeapLayout.SlotPayload];
        uint kind = source[entry + WarpPortableHeapLayout.SlotKind];
        uint type = Type(source, source[entry + WarpPortableHeapLayout.SlotType]);
        if (kind == WarpPortableHeapLayout.ReferenceArray)
        {
            for (uint index = 0; index < source[entry + WarpPortableHeapLayout.SlotLength]; index++)
            {
                if (RewriteReference(source, destination, sourcePayload + index * 3, targetPayload + index * 3) != 0)
                {
                    return destination[WarpPortableHeapLayout.Fault];
                }
            }
            return 0;
        }
        if (kind == WarpPortableHeapLayout.ValueArray)
        {
            uint element = Type(source, source[type + WarpPortableHeapLayout.ElementType]);
            for (uint index = 0; index < source[entry + WarpPortableHeapLayout.SlotLength]; index++)
            {
                uint offset = index * source[type + WarpPortableHeapLayout.ElementWords];
                if (RewriteMap(source, destination, sourcePayload + offset, targetPayload + offset,
                    source[element + WarpPortableHeapLayout.ReferenceMap], source[element + WarpPortableHeapLayout.ReferenceCount]) != 0)
                {
                    return destination[WarpPortableHeapLayout.Fault];
                }
            }
            return 0;
        }
        return RewriteMap(source, destination, sourcePayload, targetPayload,
            source[type + WarpPortableHeapLayout.ReferenceMap], source[type + WarpPortableHeapLayout.ReferenceCount]);
    }

    private static uint RewriteMap(uint[] source, uint[] destination, uint sourcePayload, uint targetPayload, uint map, uint count)
    {
        for (uint index = 0; index < count; index++)
        {
            uint offset = source[map + index * 2];
            if (RewriteReference(source, destination, sourcePayload + offset, targetPayload + offset) != 0)
            {
                return destination[WarpPortableHeapLayout.Fault];
            }
        }
        return 0;
    }

    private static uint RewriteReference(uint[] source, uint[] destination, uint sourceReference, uint targetReference)
    {
        uint context = source[sourceReference];
        uint slot = source[sourceReference + 1];
        uint generation = source[sourceReference + 2];
        if (RequireReference(source, context, slot, generation, 1) != 0)
        {
            return ForwardFault(source, destination);
        }
        if (slot == 0)
        {
            return 0;
        }
        uint targetSlot = CloneObject(source, destination, slot, generation);
        if (destination[WarpPortableHeapLayout.Fault] != 0)
        {
            return destination[WarpPortableHeapLayout.Fault];
        }
        destination[targetReference] = destination[WarpPortableHeapLayout.Context];
        destination[targetReference + 1] = targetSlot;
        destination[targetReference + 2] = destination[Slot(destination, targetSlot) + WarpPortableHeapLayout.SlotGeneration];
        return 0;
    }
}
