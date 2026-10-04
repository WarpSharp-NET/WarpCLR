namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    private static uint Allocate(uint[] arena, uint typeId, uint kind, uint length)
    {
        if (arena[WarpPortableHeapLayout.CollectionState] != WarpPortableHeapLayout.Idle)
        {
            return Fail(arena, WarpPortableHeapLayout.Busy, arena[WarpPortableHeapLayout.CollectionState], 0);
        }
        uint words = PayloadWords(arena, typeId, kind, length);
        if (arena[WarpPortableHeapLayout.Fault] != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (arena[WarpPortableHeapLayout.AllocationQuota] < WarpPortableHeapLayout.BlockWords + 1 ||
            words > arena[WarpPortableHeapLayout.AllocationQuota] - WarpPortableHeapLayout.BlockWords)
        {
            return Fail(arena, WarpPortableHeapLayout.OutOfMemory, words, arena[WarpPortableHeapLayout.AllocationQuota]);
        }
        uint requested = words == 0 ? WarpPortableHeapLayout.BlockWords + 1 : words + WarpPortableHeapLayout.BlockWords;
        uint slot = 1;
        while (slot <= arena[WarpPortableHeapLayout.SlotCount] && arena[Slot(arena, slot) + WarpPortableHeapLayout.SlotState] != WarpPortableHeapLayout.Free)
        {
            slot++;
        }
        if (slot > arena[WarpPortableHeapLayout.SlotCount])
        {
            return Fail(arena, WarpPortableHeapLayout.OutOfMemory, requested, 0);
        }
        uint block = FindBlock(arena, requested);
        if (block == 0)
        {
            return Fail(arena, WarpPortableHeapLayout.OutOfMemory, requested, arena[WarpPortableHeapLayout.AllocationQuota] - arena[WarpPortableHeapLayout.UsedWords]);
        }
        InitializeObject(arena, slot, typeId, kind, length, words, block, requested);
        return 0;
    }

    private static uint PayloadWords(uint[] arena, uint typeId, uint kind, uint length)
    {
        uint type = Type(arena, typeId);
        uint maximum = arena[WarpPortableHeapLayout.AllocationQuota];
        if (kind == WarpPortableHeapLayout.String)
        {
            return (length >> 1) + (length & 1u);
        }
        if (kind == WarpPortableHeapLayout.Class || kind == WarpPortableHeapLayout.Box)
        {
            return arena[type + WarpPortableHeapLayout.TypePayloadWords];
        }
        uint stride = arena[type + WarpPortableHeapLayout.ElementWords];
        uint words = length * stride;
        if ((length != 0 && WarpPortableWordMath.MultiplyHigh(length, stride) != 0) || words > maximum)
        {
            Fail(arena, WarpPortableHeapLayout.OutOfMemory, length, stride);
            return 0;
        }
        return words;
    }

    private static uint FindBlock(uint[] arena, uint requested)
    {
        uint block = arena[WarpPortableHeapLayout.DataStart];
        uint remaining = arena[WarpPortableHeapLayout.AllocationQuota] - arena[WarpPortableHeapLayout.UsedWords];
        while (block < (uint)arena.Length)
        {
            uint size = arena[block + WarpPortableHeapLayout.BlockSize];
            uint charge = size - requested < WarpPortableHeapLayout.BlockWords + 1 ? size : requested;
            if (arena[block + WarpPortableHeapLayout.BlockSlot] == 0 && size >= requested && charge <= remaining)
            {
                return block;
            }
            block += size;
        }
        return 0;
    }

    private static uint InitializeObject(uint[] arena, uint slot, uint typeId, uint kind, uint length, uint words, uint block, uint requested)
    {
        uint size = arena[block + WarpPortableHeapLayout.BlockSize];
        if (size - requested >= WarpPortableHeapLayout.BlockWords + 1)
        {
            arena[block + requested + WarpPortableHeapLayout.BlockSize] = size - requested;
            arena[block + requested + WarpPortableHeapLayout.BlockSlot] = 0;
            arena[block + WarpPortableHeapLayout.BlockSize] = requested;
            size = requested;
        }
        arena[block + WarpPortableHeapLayout.BlockSlot] = slot;
        for (uint word = WarpPortableHeapLayout.BlockWords; word < size; word++)
        {
            arena[block + word] = 0;
        }
        uint entry = Slot(arena, slot);
        arena[entry + WarpPortableHeapLayout.SlotState] = WarpPortableHeapLayout.Allocated;
        arena[entry + WarpPortableHeapLayout.SlotType] = typeId;
        arena[entry + WarpPortableHeapLayout.SlotPayload] = block + WarpPortableHeapLayout.BlockWords;
        arena[entry + WarpPortableHeapLayout.SlotPayloadWords] = words;
        arena[entry + WarpPortableHeapLayout.SlotLength] = length;
        arena[entry + WarpPortableHeapLayout.SlotKind] = kind;
        arena[entry + WarpPortableHeapLayout.SlotMark] = 0;
        arena[WarpPortableHeapLayout.UsedWords] += size;
        arena[WarpPortableHeapLayout.LiveObjects]++;
        SetReferenceResult(arena, arena[WarpPortableHeapLayout.Context], slot, arena[entry + WarpPortableHeapLayout.SlotGeneration]);
        return 0;
    }

    private static uint ReleaseObject(uint[] arena, uint slot)
    {
        uint entry = Slot(arena, slot);
        uint block = arena[entry + WarpPortableHeapLayout.SlotPayload] - WarpPortableHeapLayout.BlockWords;
        arena[block + WarpPortableHeapLayout.BlockSlot] = 0;
        arena[WarpPortableHeapLayout.UsedWords] -= arena[block + WarpPortableHeapLayout.BlockSize];
        arena[WarpPortableHeapLayout.LiveObjects]--;
        arena[entry + WarpPortableHeapLayout.SlotState] = arena[entry + WarpPortableHeapLayout.SlotGeneration] == 0xFFFFFFFFu ?
            WarpPortableHeapLayout.Retired : WarpPortableHeapLayout.Free;
        if (arena[entry + WarpPortableHeapLayout.SlotState] == WarpPortableHeapLayout.Free)
        {
            arena[entry + WarpPortableHeapLayout.SlotGeneration]++;
        }
        return 0;
    }

    private static uint Coalesce(uint[] arena)
    {
        uint block = arena[WarpPortableHeapLayout.DataStart];
        while (block < (uint)arena.Length)
        {
            uint size = arena[block + WarpPortableHeapLayout.BlockSize];
            uint next = block + size;
            if (arena[block + WarpPortableHeapLayout.BlockSlot] == 0 && next < (uint)arena.Length && arena[next + WarpPortableHeapLayout.BlockSlot] == 0)
            {
                arena[block + WarpPortableHeapLayout.BlockSize] = size + arena[next + WarpPortableHeapLayout.BlockSize];
            }
            else
            {
                block = next;
            }
        }
        return 0;
    }
}
