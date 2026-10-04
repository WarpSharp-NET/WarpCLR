namespace WarpCLR.Compiler;

internal static partial class WarpPortableSchedulerServices
{
    public static uint PublishRoots(uint[] arena, uint scheduler, uint controller, uint worker, uint generation,
        uint epoch, uint map, uint revision, uint function, uint pc)
    {
        uint status = Begin(arena, scheduler, controller, 10);
        if (status != 0)
        {
            return status;
        }
        if (RequireWorker(arena, scheduler, worker) != 0 || map >= arena[scheduler + WarpPortableSchedulerLayout.RootMapCount] ||
            epoch != arena[scheduler + WarpPortableSchedulerLayout.GCEpoch])
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, epoch);
        }
        uint entry = Worker(arena, scheduler, worker);
        uint prior = arena[entry + WarpPortableSchedulerLayout.RootRevision];
        if (Terminal(arena[entry + WarpPortableSchedulerLayout.WorkerState]) != 0 ||
            arena[entry + WarpPortableSchedulerLayout.RunGeneration] != generation)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, revision);
        }
        if (prior == 0xFFFFFFFFu)
        {
            return RuntimeFault(arena, scheduler, worker, WarpPortableSchedulerLayout.GenerationExhausted);
        }
        if (revision == 0 || revision != prior + 1)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, revision);
        }
        uint rootMap = RootMap(arena, scheduler, map);
        if (arena[rootMap + WarpPortableSchedulerLayout.MapFunction] != function || arena[rootMap + WarpPortableSchedulerLayout.MapPC] != pc ||
            ValidatePublishedReferences(arena, scheduler, entry, rootMap) != 0)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, map, pc);
        }
        CopyPublishedReferences(arena, scheduler, entry, rootMap);
        arena[entry + WarpPortableSchedulerLayout.RootEpoch] = epoch;
        arena[entry + WarpPortableSchedulerLayout.RootMap] = map;
        arena[entry + WarpPortableSchedulerLayout.RootRevision] = revision;
        arena[entry + WarpPortableSchedulerLayout.RootLiveWords] = arena[rootMap + WarpPortableSchedulerLayout.MapReferenceCount] * 3;
        arena[entry + WarpPortableSchedulerLayout.ContinuationFunction] = function;
        arena[entry + WarpPortableSchedulerLayout.ContinuationPC] = pc;
        arena[entry + WarpPortableSchedulerLayout.SafePoint] = 1;
        return 0;
    }

    private static uint ValidatePublishedReferences(uint[] arena, uint scheduler, uint worker, uint map)
    {
        uint count = arena[map + WarpPortableSchedulerLayout.MapReferenceCount];
        uint offsets = scheduler + arena[map + WarpPortableSchedulerLayout.MapOffsets];
        uint source = arena[worker + WarpPortableSchedulerLayout.LogicalStackBase];
        uint quota = arena[scheduler + WarpPortableSchedulerLayout.LogicalStateWords];
        if (count > arena[worker + WarpPortableSchedulerLayout.FrameRootCapacity] || source > (uint)arena.Length ||
            quota > (uint)arena.Length - source)
        {
            return WarpPortableSchedulerLayout.Invalid;
        }
        for (uint index = 0; index < count; index++)
        {
            uint offset = arena[offsets + index];
            if (quota < 3 || offset > quota - 3 ||
                (arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] != 0 &&
                    ValidateHeapReference(arena, arena[source + offset], arena[source + offset + 1], arena[source + offset + 2]) != 0))
            {
                return WarpPortableSchedulerLayout.Invalid;
            }
        }
        return 0;
    }

    private static uint ValidateHeapReference(uint[] arena, uint context, uint slot, uint generation)
    {
        if ((context | slot | generation) == 0)
        {
            return 0;
        }
        if (context != arena[WarpPortableHeapLayout.Context] || slot == 0 || slot > arena[WarpPortableHeapLayout.SlotCount] || generation == 0)
        {
            return WarpPortableSchedulerLayout.Invalid;
        }
        uint entry = arena[WarpPortableHeapLayout.SlotStart] + (slot - 1) * WarpPortableHeapLayout.SlotWords;
        return arena[entry + WarpPortableHeapLayout.SlotState] == WarpPortableHeapLayout.Allocated &&
            arena[entry + WarpPortableHeapLayout.SlotGeneration] == generation ? 0 : WarpPortableSchedulerLayout.Invalid;
    }

    private static uint CopyPublishedReferences(uint[] arena, uint scheduler, uint worker, uint map)
    {
        if (arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] == 0)
        {
            return 0;
        }
        uint count = arena[map + WarpPortableSchedulerLayout.MapReferenceCount];
        uint offsets = scheduler + arena[map + WarpPortableSchedulerLayout.MapOffsets];
        uint source = arena[worker + WarpPortableSchedulerLayout.LogicalStackBase];
        uint root = arena[worker + WarpPortableSchedulerLayout.FrameRootStart];
        for (uint index = 0; index < arena[worker + WarpPortableSchedulerLayout.FrameRootCapacity]; index++)
        {
            uint reference = arena[WarpPortableHeapLayout.RootStart] + (root + index - 1) * WarpPortableHeapLayout.RootWords + WarpPortableHeapLayout.RootReference;
            if (index < count)
            {
                uint value = source + arena[offsets + index];
                arena[reference] = arena[value];
                arena[reference + 1] = arena[value + 1];
                arena[reference + 2] = arena[value + 2];
            }
            else
            {
                arena[reference] = 0;
                arena[reference + 1] = 0;
                arena[reference + 2] = 0;
            }
        }
        return 0;
    }
}
