namespace WarpCLR.Compiler;

internal static partial class WarpPortableHeapServices
{
    public static uint EnterWorker(uint[] arena, uint worker)
    {
        if (Begin(arena, 19) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (worker >= arena[WarpPortableHeapLayout.WorkerCount])
        {
            return Fail(arena, WarpPortableHeapLayout.Quota, worker, arena[WarpPortableHeapLayout.WorkerCount]);
        }
        uint entry = arena[WarpPortableHeapLayout.WorkerStart] + worker * WarpPortableHeapLayout.WorkerWords;
        if (arena[entry] != 0 || arena[WarpPortableHeapLayout.CollectionState] != WarpPortableHeapLayout.Idle)
        {
            return Fail(arena, WarpPortableHeapLayout.Busy, worker, arena[WarpPortableHeapLayout.CollectionState]);
        }
        arena[entry] = 1;
        arena[WarpPortableHeapLayout.ActiveWorkers]++;
        return 0;
    }

    public static uint ExitWorker(uint[] arena, uint worker)
    {
        if (Begin(arena, 20) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (worker >= arena[WarpPortableHeapLayout.WorkerCount])
        {
            return Fail(arena, WarpPortableHeapLayout.Quota, worker, arena[WarpPortableHeapLayout.WorkerCount]);
        }
        uint entry = arena[WarpPortableHeapLayout.WorkerStart] + worker * WarpPortableHeapLayout.WorkerWords;
        if (arena[entry] == 0 || (arena[WarpPortableHeapLayout.LeaseState] != 0 && arena[WarpPortableHeapLayout.LeaseOwner] == worker))
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, worker, 0);
        }
        if (arena[entry] == 2)
        {
            arena[WarpPortableHeapLayout.ParkedWorkers]--;
        }
        arena[entry] = 0;
        arena[WarpPortableHeapLayout.ActiveWorkers]--;
        return 0;
    }

    public static uint RequestCollection(uint[] arena)
    {
        if (arena[WarpPortableHeapLayout.PendingResult] == 0 && Begin(arena, 21) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (arena[WarpPortableHeapLayout.CollectionState] == WarpPortableHeapLayout.Requested)
        {
            return 0;
        }
        if (arena[WarpPortableHeapLayout.CollectionState] != WarpPortableHeapLayout.Idle)
        {
            return Fail(arena, WarpPortableHeapLayout.Busy, arena[WarpPortableHeapLayout.CollectionState], 0);
        }
        if (arena[WarpPortableHeapLayout.CollectionEpoch] == 0xFFFFFFFFu)
        {
            return Fail(arena, WarpPortableHeapLayout.Quota, arena[WarpPortableHeapLayout.CollectionEpoch], 0);
        }
        arena[WarpPortableHeapLayout.CollectionEpoch]++;
        arena[WarpPortableHeapLayout.CollectionState] = WarpPortableHeapLayout.Requested;
        for (uint worker = 0; worker < arena[WarpPortableHeapLayout.WorkerCount]; worker++)
        {
            uint entry = arena[WarpPortableHeapLayout.WorkerStart] + worker * WarpPortableHeapLayout.WorkerWords;
            if (arena[entry] == 2)
            {
                arena[entry + 1] = arena[WarpPortableHeapLayout.CollectionEpoch];
            }
        }
        return 0;
    }

    public static uint ParkWorker(uint[] arena, uint worker)
    {
        if (Begin(arena, 22) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (worker >= arena[WarpPortableHeapLayout.WorkerCount])
        {
            return Fail(arena, WarpPortableHeapLayout.Quota, worker, arena[WarpPortableHeapLayout.WorkerCount]);
        }
        uint entry = arena[WarpPortableHeapLayout.WorkerStart] + worker * WarpPortableHeapLayout.WorkerWords;
        if (arena[WarpPortableHeapLayout.LeaseState] != 0 && arena[WarpPortableHeapLayout.LeaseOwner] == worker)
        {
            return Fail(arena, WarpPortableHeapLayout.Busy, worker, arena[WarpPortableHeapLayout.LeaseEpoch]);
        }
        if (arena[entry] != 1 || arena[WarpPortableHeapLayout.CollectionState] != WarpPortableHeapLayout.Requested)
        {
            return Fail(arena, WarpPortableHeapLayout.InvalidOperation, worker, arena[WarpPortableHeapLayout.CollectionState]);
        }
        arena[entry] = 2;
        arena[entry + 1] = arena[WarpPortableHeapLayout.CollectionEpoch];
        arena[WarpPortableHeapLayout.ParkedWorkers]++;
        return 0;
    }

    public static uint ResumeWorker(uint[] arena, uint worker)
    {
        if (Begin(arena, 23) != 0)
        {
            return arena[WarpPortableHeapLayout.Fault];
        }
        if (worker >= arena[WarpPortableHeapLayout.WorkerCount])
        {
            return Fail(arena, WarpPortableHeapLayout.Quota, worker, arena[WarpPortableHeapLayout.WorkerCount]);
        }
        uint entry = arena[WarpPortableHeapLayout.WorkerStart] + worker * WarpPortableHeapLayout.WorkerWords;
        if (arena[entry] != 2 || arena[WarpPortableHeapLayout.CollectionState] != WarpPortableHeapLayout.Idle)
        {
            return Fail(arena, WarpPortableHeapLayout.Busy, worker, arena[WarpPortableHeapLayout.CollectionState]);
        }
        arena[entry] = 1;
        arena[WarpPortableHeapLayout.ParkedWorkers]--;
        return 0;
    }
}
