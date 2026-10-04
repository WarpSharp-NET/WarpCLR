namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    public static uint TransferGraph(uint[] source, uint[] destination, uint context, uint slot, uint generation)
    {
        if (Begin(destination, 29) != 0)
        {
            return destination[WarpPortableHeapLayout.Fault];
        }
        if (source[WarpPortableHeapLayout.Context] == destination[WarpPortableHeapLayout.Context])
        {
            return Fail(destination, WarpPortableHeapLayout.WrongContext, context, destination[WarpPortableHeapLayout.Context]);
        }
        if (RequireReference(source, context, slot, generation, 1) != 0)
        {
            return ForwardFault(source, destination);
        }
        if (slot == 0)
        {
            return 0;
        }
        if (source[WarpPortableHeapLayout.ActiveWorkers] != source[WarpPortableHeapLayout.ParkedWorkers] ||
            destination[WarpPortableHeapLayout.ActiveWorkers] != destination[WarpPortableHeapLayout.ParkedWorkers] ||
            source[WarpPortableHeapLayout.CollectionState] != WarpPortableHeapLayout.Idle || destination[WarpPortableHeapLayout.CollectionState] != WarpPortableHeapLayout.Idle)
        {
            return Fail(destination, WarpPortableHeapLayout.Busy, context, slot);
        }
        for (uint word = 0; word < 8; word++)
        {
            if (source[WarpPortableHeapLayout.SchemaHash + word] != destination[WarpPortableHeapLayout.SchemaHash + word])
            {
                return Fail(destination, WarpPortableHeapLayout.InvalidType, word, 0);
            }
        }
        destination[WarpPortableHeapLayout.TransferCount] = 0;
        uint root = CloneObject(source, destination, slot, generation);
        uint fault = destination[WarpPortableHeapLayout.Fault];
        for (uint index = 0; fault == 0 && index < destination[WarpPortableHeapLayout.TransferCount]; index++)
        {
            fault = RewriteObjectReferences(source, destination, index);
        }
        if (fault != 0)
        {
            RollbackTransfer(destination);
            return fault;
        }
        destination[WarpPortableHeapLayout.TransferCount] = 0;
        SetReferenceResult(destination, destination[WarpPortableHeapLayout.Context], root, destination[Slot(destination, root) + WarpPortableHeapLayout.SlotGeneration]);
        return 0;
    }

    private static uint CloneObject(uint[] source, uint[] destination, uint slot, uint generation)
    {
        uint start = destination[WarpPortableHeapLayout.TransferStart];
        for (uint index = 0; index < destination[WarpPortableHeapLayout.TransferCount]; index++)
        {
            uint mapping = start + index * WarpPortableHeapLayout.TransferWords;
            if (destination[mapping] == slot && destination[mapping + 1] == generation)
            {
                return destination[mapping + 2];
            }
        }
        if (destination[WarpPortableHeapLayout.TransferCount] == destination[WarpPortableHeapLayout.TransferCapacity])
        {
            Fail(destination, WarpPortableHeapLayout.Quota, destination[WarpPortableHeapLayout.TransferCapacity], 0);
            return 0;
        }
        uint entry = Slot(source, slot);
        if (Allocate(destination, source[entry + WarpPortableHeapLayout.SlotType], source[entry + WarpPortableHeapLayout.SlotKind], source[entry + WarpPortableHeapLayout.SlotLength]) != 0)
        {
            return 0;
        }
        uint cloned = destination[WarpPortableHeapLayout.Result + 1];
        uint target = Slot(destination, cloned);
        uint sourcePayload = source[entry + WarpPortableHeapLayout.SlotPayload];
        uint destinationPayload = destination[target + WarpPortableHeapLayout.SlotPayload];
        for (uint word = 0; word < source[entry + WarpPortableHeapLayout.SlotPayloadWords]; word++)
        {
            destination[destinationPayload + word] = source[sourcePayload + word];
        }
        uint map = start + destination[WarpPortableHeapLayout.TransferCount] * WarpPortableHeapLayout.TransferWords;
        destination[map] = slot;
        destination[map + 1] = generation;
        destination[map + 2] = cloned;
        destination[map + 3] = destination[target + WarpPortableHeapLayout.SlotGeneration];
        destination[WarpPortableHeapLayout.TransferCount]++;
        return cloned;
    }

    private static uint ForwardFault(uint[] source, uint[] destination) =>
        Fail(destination, source[WarpPortableHeapLayout.Fault], source[WarpPortableHeapLayout.Argument0], source[WarpPortableHeapLayout.Argument1]);

    private static uint RollbackTransfer(uint[] destination)
    {
        for (uint index = 0; index < destination[WarpPortableHeapLayout.TransferCount]; index++)
        {
            uint mapping = destination[WarpPortableHeapLayout.TransferStart] + index * WarpPortableHeapLayout.TransferWords;
            ReleaseObject(destination, destination[mapping + 2]);
        }
        destination[WarpPortableHeapLayout.TransferCount] = 0;
        Coalesce(destination);
        for (uint word = 0; word < 8; word++)
        {
            destination[WarpPortableHeapLayout.Result + word] = 0;
        }
        return 0;
    }
}
