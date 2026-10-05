namespace WarpCLR.Compiler;

internal static partial class WarpPortableSourceFaultServices
{
    private static uint ValidRoot(uint[] arena, uint root, uint generation)
    {
        if (root == 0 || root > arena[WarpPortableHeapLayout.RootCount] || generation == 0) { return 0; }
        uint row = Root(arena, root);
        return arena[row + WarpPortableHeapLayout.RootOwnership] == WarpPortableHeapLayout.RuntimeOwnedRoot &&
            arena[row + WarpPortableHeapLayout.RootState] == WarpPortableHeapLayout.Allocated &&
            arena[row + WarpPortableHeapLayout.RootGeneration] == generation && arena[row + WarpPortableHeapLayout.RootKind] == WarpPortableHeapLayout.StrongRoot &&
            arena[row + WarpPortableHeapLayout.RootInteriorOffset] == 0 && arena[row + WarpPortableHeapLayout.RootInteriorWords] == 0 &&
            arena[row + WarpPortableHeapLayout.RootInteriorType] == 0 ? 1u : 0u;
    }

    private static uint RootMatches(uint[] arena, uint root, uint owner)
    {
        uint row = Root(arena, root) + WarpPortableHeapLayout.RootReference;
        return arena[row] == arena[owner] && arena[row + 1] == arena[owner + 1] && arena[row + 2] == arena[owner + 2] ? 1u : 0u;
    }

    private static uint NullRoot(uint[] arena, uint root)
    {
        uint row = Root(arena, root) + WarpPortableHeapLayout.RootReference;
        return (arena[row] | arena[row + 1] | arena[row + 2]) == 0 ? 1u : 0u;
    }

    private static uint ValidateRootPublication(uint[] arena, uint worker, uint epoch, uint revision, uint function, uint offset, uint opcode)
    {
        uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
        uint entry = scheduler + arena[scheduler + WarpPortableSchedulerLayout.WorkerStart] + worker * WarpPortableSchedulerLayout.WorkerWords;
        if (arena[scheduler + WarpPortableSchedulerLayout.GCEpoch] != epoch || arena[entry + WarpPortableSchedulerLayout.RootEpoch] != epoch ||
            arena[entry + WarpPortableSchedulerLayout.RootRevision] != revision || arena[scheduler + WarpPortableSchedulerLayout.ServiceRootRevision] != revision ||
            arena[scheduler + WarpPortableSchedulerLayout.ServiceOwner] != worker || arena[scheduler + WarpPortableSchedulerLayout.ServiceEpoch] != epoch ||
            arena[WarpPortableHeapLayout.CollectionEpoch] != epoch || arena[WarpPortableHeapLayout.LeaseEpoch] != epoch ||
            arena[WarpPortableHeapLayout.PendingResult] != 0 || arena[scheduler + WarpPortableSchedulerLayout.ServicePending] != 0)
        { return WarpPortableExceptionLayout.InvalidTicket; }
        uint map = arena[entry + WarpPortableSchedulerLayout.RootMap], count = arena[scheduler + WarpPortableSchedulerLayout.RootMapCount];
        uint start = arena[scheduler + WarpPortableSchedulerLayout.RootMapStart];
        if (map >= count || start > (uint)arena.Length - scheduler || Fits(count, WarpPortableSchedulerLayout.RootMapWords, (uint)arena.Length - scheduler - start) == 0)
        { return WarpPortableExceptionLayout.InvalidDescriptor; }
        uint row = scheduler + start + map * WarpPortableSchedulerLayout.RootMapWords;
        if (arena[row + WarpPortableSchedulerLayout.MapFunction] != function || arena[entry + WarpPortableSchedulerLayout.ContinuationFunction] != function ||
            arena[row + WarpPortableSchedulerLayout.MapPC] != arena[entry + WarpPortableSchedulerLayout.ContinuationPC] ||
            WarpPortableExceptionServices.FaultPcMatches(arena, function, arena[row + WarpPortableSchedulerLayout.MapPC], offset, opcode) == 0)
        { return WarpPortableExceptionLayout.InvalidContinuation; }
        uint references = arena[row + WarpPortableSchedulerLayout.MapReferenceCount], capacity = arena[entry + WarpPortableSchedulerLayout.FrameRootCapacity];
        uint first = arena[entry + WarpPortableSchedulerLayout.FrameRootStart], bank = arena[entry + WarpPortableSchedulerLayout.LogicalStackBase];
        uint words = arena[scheduler + WarpPortableSchedulerLayout.LogicalStateWords], offsets = arena[row + WarpPortableSchedulerLayout.MapOffsets];
        if (references > capacity || arena[entry + WarpPortableSchedulerLayout.RootLiveWords] != references * 3 ||
            Fits(references, 3, uint.MaxValue) == 0 || Range(arena, bank, words) == 0 ||
            offsets > (uint)arena.Length - scheduler || Range(arena, scheduler + offsets, references) == 0 ||
            first == 0 || first > arena[WarpPortableHeapLayout.RootCount] || capacity > arena[WarpPortableHeapLayout.RootCount] - first + 1)
        { return WarpPortableExceptionLayout.InvalidOwnership; }
        uint pool = arena[WarpPortableSourceFaultFactoryLayout.Descriptor], poolFirst = arena[pool + WarpPortableSourceFaultFactoryLayout.RootFirst];
        uint poolCount = arena[pool + WarpPortableSourceFaultFactoryLayout.RootCount];
        if (first < poolFirst ? capacity > poolFirst - first : first - poolFirst < poolCount) { return WarpPortableExceptionLayout.InvalidOwnership; }
        for (uint index = 0; index < capacity; index++)
        {
            uint root = first + index;
            if (ValidRoot(arena, root, arena[Root(arena, root) + WarpPortableHeapLayout.RootGeneration]) == 0) { return WarpPortableExceptionLayout.InvalidOwnership; }
            if (index >= references)
            {
                if (NullRoot(arena, root) == 0) { return WarpPortableExceptionLayout.InvalidOwnership; }
                continue;
            }
            uint relative = arena[scheduler + offsets + index];
            if (words < 3 || relative > words - 3 || RootMatches(arena, root, bank + relative) == 0) { return WarpPortableExceptionLayout.InvalidOwnership; }
            uint owner = bank + relative;
            if ((arena[owner] | arena[owner + 1] | arena[owner + 2]) != 0 && Object(arena, arena[owner], arena[owner + 1], arena[owner + 2]) == 0)
            { return WarpPortableExceptionLayout.InvalidReference; }
        }
        return 0;
    }
}
