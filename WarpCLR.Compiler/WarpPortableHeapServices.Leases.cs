namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    public static uint AcquireServiceLease(uint[] arena, uint owner)
    {
        if (arena[WarpPortableHeapLayout.LeaseState] != 0 || arena[WarpPortableHeapLayout.PendingResult] != 0 ||
            arena[WarpPortableHeapLayout.CollectionState] != WarpPortableHeapLayout.Idle)
        {
            return Fail(arena, WarpPortableHeapLayout.Busy, owner, arena[WarpPortableHeapLayout.LeaseOwner]);
        }
        if (owner != 0xFFFFFFFFu)
        {
            if (owner >= arena[WarpPortableHeapLayout.WorkerCount])
            {
                return Fail(arena, WarpPortableHeapLayout.Quota, owner, arena[WarpPortableHeapLayout.WorkerCount]);
            }
            uint worker = arena[WarpPortableHeapLayout.WorkerStart] + owner * WarpPortableHeapLayout.WorkerWords;
            if (arena[worker] != 1)
            {
                return Fail(arena, WarpPortableHeapLayout.InvalidOperation, owner, arena[worker]);
            }
        }
        arena[WarpPortableHeapLayout.LeaseState] = 1;
        arena[WarpPortableHeapLayout.LeaseOwner] = owner;
        arena[WarpPortableHeapLayout.LeaseEpoch] = arena[WarpPortableHeapLayout.CollectionEpoch];
        arena[WarpPortableHeapLayout.Fault] = 0;
        return 0;
    }

    public static uint AcknowledgeServiceResult(uint[] arena, uint owner)
    {
        if (arena[WarpPortableHeapLayout.LeaseState] == 0 || arena[WarpPortableHeapLayout.LeaseOwner] != owner)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, owner, arena[WarpPortableHeapLayout.LeaseOwner]);
        }
        arena[WarpPortableHeapLayout.PendingResult] = 0;
        arena[WarpPortableHeapLayout.Fault] = 0;
        return 0;
    }

    public static uint ReleaseServiceLease(uint[] arena, uint owner)
    {
        if (arena[WarpPortableHeapLayout.LeaseState] == 0 || arena[WarpPortableHeapLayout.LeaseOwner] != owner)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, owner, arena[WarpPortableHeapLayout.LeaseOwner]);
        }
        if (arena[WarpPortableHeapLayout.PendingResult] != 0)
        {
            return Fail(arena, WarpPortableHeapLayout.Busy, owner, arena[WarpPortableHeapLayout.LeaseEpoch]);
        }
        arena[WarpPortableHeapLayout.LeaseState] = 0;
        arena[WarpPortableHeapLayout.Fault] = 0;
        return 0;
    }

    public static uint AbortServiceLease(uint[] arena, uint owner)
    {
        if (arena[WarpPortableHeapLayout.LeaseState] == 0 || arena[WarpPortableHeapLayout.LeaseOwner] != owner)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, owner, arena[WarpPortableHeapLayout.LeaseOwner]);
        }
        arena[WarpPortableHeapLayout.PendingResult] = 0;
        arena[WarpPortableHeapLayout.LeaseState] = 0;
        for (uint word = 0; word < 8; word++)
        {
            arena[WarpPortableHeapLayout.Result + word] = 0;
        }
        return 0;
    }
}
