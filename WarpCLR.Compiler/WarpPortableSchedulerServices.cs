namespace WarpCLR.Compiler;

// Atomic controller acquisition/release belongs to the caller. These routines
// never wait for another physical or logical worker and never execute user code.
internal static partial class WarpPortableSchedulerServices
{
    private static uint Begin(uint[] arena, uint scheduler, uint controller, uint operation)
    {
        uint length = (uint)arena.Length;
        if (scheduler > length || length - scheduler < WarpPortableSchedulerLayout.HeaderWords)
        {
            return WarpPortableSchedulerLayout.Invalid;
        }
        if (controller == 0 || arena[scheduler + WarpPortableSchedulerLayout.ControllerOwner] != controller)
        {
            return WarpPortableSchedulerLayout.Yield;
        }
        uint words = arena[scheduler + WarpPortableSchedulerLayout.DescriptorWords];
        if (arena[scheduler] != WarpPortableSchedulerLayout.Magic || arena[scheduler + 1] != WarpPortableSchedulerLayout.Version ||
            arena[scheduler + WarpPortableSchedulerLayout.ArenaWords] != length ||
            words < WarpPortableSchedulerLayout.HeaderWords || words > length - scheduler ||
            ValidTable(arena, scheduler, WarpPortableSchedulerLayout.WorkerStart, WarpPortableSchedulerLayout.WorkerCount, 6) == 0 ||
            ValidTable(arena, scheduler, WarpPortableSchedulerLayout.PhysicalStart, WarpPortableSchedulerLayout.ResidentCount, 2) == 0 ||
            ValidTable(arena, scheduler, WarpPortableSchedulerLayout.BarrierStart, WarpPortableSchedulerLayout.BarrierCount, 3) == 0 ||
            ValidTable(arena, scheduler, WarpPortableSchedulerLayout.RootMapStart, WarpPortableSchedulerLayout.RootMapCount, 2) == 0 ||
            ValidateSchemaTables(arena, scheduler) == 0 || ValidateHeapBinding(arena, scheduler) == 0)
        {
            return WarpPortableSchedulerLayout.Invalid;
        }
        if (arena[scheduler + WarpPortableSchedulerLayout.GCState] == WarpPortableSchedulerLayout.GCCollecting && operation != 14)
        {
            return WarpPortableSchedulerLayout.Yield;
        }
        arena[scheduler + WarpPortableSchedulerLayout.Status] = 0;
        arena[scheduler + WarpPortableSchedulerLayout.Operation] = operation;
        arena[scheduler + WarpPortableSchedulerLayout.Argument0] = 0;
        arena[scheduler + WarpPortableSchedulerLayout.Argument1] = 0;
        for (uint word = 0; word < 8; word++)
        {
            arena[scheduler + WarpPortableSchedulerLayout.Result + word] = 0;
        }
        if (arena[scheduler + WarpPortableSchedulerLayout.ContextState] == WarpPortableSchedulerLayout.DisposedContext)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.ContextDisposed, 0, 0);
        }
        return 0;
    }

    private static uint ValidateHeapBinding(uint[] arena, uint scheduler)
    {
        uint linked = arena[scheduler + WarpPortableSchedulerLayout.HeapLinked];
        if (linked == 0)
        {
            return 1;
        }
        if (linked != 1 || scheduler < WarpPortableHeapLayout.HeaderWords || arena[0] != WarpPortableHeapLayout.Magic ||
            arena[1] != WarpPortableHeapLayout.Version || arena[WarpPortableSchedulerLayout.HeapDescriptor] != scheduler ||
            arena[WarpPortableHeapLayout.Context] != arena[scheduler + WarpPortableSchedulerLayout.Context] ||
            arena[WarpPortableHeapLayout.WorkerCount] != arena[scheduler + WarpPortableSchedulerLayout.WorkerCount] ||
            ValidateHeapTables(arena, scheduler) == 0)
        {
            return 0;
        }
        uint owner = LeaseOwner(arena, scheduler);
        return owner == WarpPortableSchedulerLayout.NoWorker ? (arena[WarpPortableHeapLayout.LeaseState] == 0 ? 1u : 0u) :
            arena[WarpPortableHeapLayout.LeaseState] == 1 && arena[WarpPortableHeapLayout.LeaseOwner] == owner &&
                arena[WarpPortableHeapLayout.LeaseEpoch] == arena[scheduler + WarpPortableSchedulerLayout.ServiceEpoch] ? 1u : 0u;
    }

    private static uint ValidateHeapTables(uint[] arena, uint scheduler)
    {
        uint length = (uint)arena.Length;
        uint roots = arena[WarpPortableHeapLayout.RootStart];
        uint slots = arena[WarpPortableHeapLayout.SlotStart];
        uint workers = arena[WarpPortableHeapLayout.WorkerStart];
        uint count = arena[scheduler + WarpPortableSchedulerLayout.WorkerCount];
        uint capacity = arena[scheduler + WarpPortableSchedulerLayout.RootSlotsPerWorker];
        return arena[3] == length && roots <= length && slots <= length && workers <= length &&
            WarpPortableWordMath.MultiplyHigh(arena[WarpPortableHeapLayout.RootCount], WarpPortableHeapLayout.RootWords) == 0 &&
            arena[WarpPortableHeapLayout.RootCount] * WarpPortableHeapLayout.RootWords <= length - roots &&
            arena[WarpPortableHeapLayout.SlotCount] <= ((length - slots) >> 3) &&
            arena[WarpPortableHeapLayout.WorkerCount] <= ((length - workers) >> 2) &&
            count * capacity <= arena[WarpPortableHeapLayout.RootCount] &&
            arena[WarpPortableHeapLayout.DataStart] >= scheduler + arena[scheduler + WarpPortableSchedulerLayout.DescriptorWords] &&
            arena[WarpPortableHeapLayout.DataStart] <= length && ValidateReservedRootOwnership(arena, scheduler) != 0 ? 1u : 0u;
    }

    private static uint ValidateSchemaTables(uint[] arena, uint scheduler)
    {
        uint workers = arena[scheduler + WarpPortableSchedulerLayout.WorkerCount];
        uint barriers = arena[scheduler + WarpPortableSchedulerLayout.BarrierCount];
        uint membership = arena[scheduler + WarpPortableSchedulerLayout.MembershipStart];
        uint maps = arena[scheduler + WarpPortableSchedulerLayout.RootMapStart];
        uint offsets = arena[scheduler + WarpPortableSchedulerLayout.RootOffsetStart];
        uint output = arena[scheduler + WarpPortableSchedulerLayout.OutputOffsets];
        uint state = arena[scheduler + WarpPortableSchedulerLayout.LogicalStateStart];
        uint quota = arena[scheduler + WarpPortableSchedulerLayout.LogicalStateWords];
        uint words = arena[scheduler + WarpPortableSchedulerLayout.DescriptorWords];
        if (workers == 0 || arena[scheduler + WarpPortableSchedulerLayout.ResidentCount] == 0 ||
            arena[scheduler + WarpPortableSchedulerLayout.ResidentCount] > workers || arena[scheduler + WarpPortableSchedulerLayout.Cursor] >= workers ||
            membership > maps || maps > offsets || offsets > output || output > state || state > words || quota == 0 ||
            arena[scheduler + WarpPortableSchedulerLayout.RootMapCount] == 0 ||
            arena[scheduler + WarpPortableSchedulerLayout.OutputReferenceCount] > state - output ||
            arena[scheduler + WarpPortableSchedulerLayout.DispatchGeneration] == 0 ||
            arena[scheduler + WarpPortableSchedulerLayout.RunningCount] > arena[scheduler + WarpPortableSchedulerLayout.ResidentCount] ||
            arena[scheduler + WarpPortableSchedulerLayout.TerminalCount] > workers ||
            arena[scheduler + WarpPortableSchedulerLayout.GCParkedCount] > workers - arena[scheduler + WarpPortableSchedulerLayout.TerminalCount] ||
            arena[scheduler + WarpPortableSchedulerLayout.OutputPendingWorkers] > workers ||
            WarpPortableWordMath.MultiplyHigh(workers, barriers) != 0 || workers * barriers > maps - membership ||
            WarpPortableWordMath.MultiplyHigh(workers, quota) != 0 || workers * quota > words - state)
        {
            return 0;
        }
        for (uint barrier = 0; barrier < barriers; barrier++)
        {
            uint start = arena[Barrier(arena, scheduler, barrier) + WarpPortableSchedulerLayout.BarrierMembership];
            if (start < membership || start > maps || workers > maps - start)
            {
                return 0;
            }
        }
        for (uint map = 0; map < arena[scheduler + WarpPortableSchedulerLayout.RootMapCount]; map++)
        {
            uint entry = RootMap(arena, scheduler, map);
            uint start = arena[entry + WarpPortableSchedulerLayout.MapOffsets];
            if (start < offsets || start > output || arena[entry + WarpPortableSchedulerLayout.MapReferenceCount] > output - start)
            {
                return 0;
            }
        }
        return ValidateWorkerBanks(arena, scheduler);
    }

    private static uint ValidateWorkerBanks(uint[] arena, uint scheduler)
    {
        uint start = scheduler + arena[scheduler + WarpPortableSchedulerLayout.LogicalStateStart];
        uint quota = arena[scheduler + WarpPortableSchedulerLayout.LogicalStateWords];
        uint capacity = arena[scheduler + WarpPortableSchedulerLayout.RootSlotsPerWorker];
        uint outputs = arena[scheduler + WarpPortableSchedulerLayout.OutputReferenceCount];
        if (capacity <= outputs || WarpPortableWordMath.MultiplyHigh(capacity, arena[scheduler + WarpPortableSchedulerLayout.WorkerCount]) != 0)
        {
            return 0;
        }
        for (uint worker = 0; worker < arena[scheduler + WarpPortableSchedulerLayout.WorkerCount]; worker++)
        {
            uint entry = Worker(arena, scheduler, worker);
            if (arena[entry + WarpPortableSchedulerLayout.LogicalStackBase] != start + worker * quota ||
                arena[entry + WarpPortableSchedulerLayout.FrameRootCapacity] != capacity - outputs - 1 ||
                (arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] != 0 &&
                    arena[entry + WarpPortableSchedulerLayout.FrameRootStart] != worker * capacity + 1))
            {
                return 0;
            }
        }
        return 1;
    }

    private static uint ValidTable(uint[] arena, uint scheduler, uint startWord, uint countWord, uint shift)
    {
        uint start = arena[scheduler + startWord];
        uint words = arena[scheduler + WarpPortableSchedulerLayout.DescriptorWords];
        return start >= WarpPortableSchedulerLayout.HeaderWords && start <= words &&
            arena[scheduler + countWord] <= ((words - start) >> (int)shift) ? 1u : 0u;
    }

    private static uint Status(uint[] arena, uint scheduler, uint status, uint argument0, uint argument1)
    {
        arena[scheduler + WarpPortableSchedulerLayout.Status] = status;
        arena[scheduler + WarpPortableSchedulerLayout.Argument0] = argument0;
        arena[scheduler + WarpPortableSchedulerLayout.Argument1] = argument1;
        return status;
    }

    private static uint Worker(uint[] arena, uint scheduler, uint worker) =>
        scheduler + arena[scheduler + WarpPortableSchedulerLayout.WorkerStart] + worker * WarpPortableSchedulerLayout.WorkerWords;

    private static uint Physical(uint[] arena, uint scheduler, uint physical) =>
        scheduler + arena[scheduler + WarpPortableSchedulerLayout.PhysicalStart] + physical * WarpPortableSchedulerLayout.PhysicalWords;

    private static uint Barrier(uint[] arena, uint scheduler, uint barrier) =>
        scheduler + arena[scheduler + WarpPortableSchedulerLayout.BarrierStart] + barrier * WarpPortableSchedulerLayout.BarrierWords;

    private static uint RootMap(uint[] arena, uint scheduler, uint map) =>
        scheduler + arena[scheduler + WarpPortableSchedulerLayout.RootMapStart] + map * WarpPortableSchedulerLayout.RootMapWords;

    private static uint RequireWorker(uint[] arena, uint scheduler, uint worker) =>
        worker < arena[scheduler + WarpPortableSchedulerLayout.WorkerCount] ? 0 :
            Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, arena[scheduler + WarpPortableSchedulerLayout.WorkerCount]);

    private static uint RequireRunning(uint[] arena, uint scheduler, uint worker, uint generation)
    {
        if (RequireWorker(arena, scheduler, worker) != 0)
        {
            return WarpPortableSchedulerLayout.Invalid;
        }
        uint entry = Worker(arena, scheduler, worker);
        if (generation == 0 || arena[entry + WarpPortableSchedulerLayout.WorkerState] != WarpPortableSchedulerLayout.Running ||
            arena[entry + WarpPortableSchedulerLayout.RunGeneration] != generation)
        {
            return Status(arena, scheduler, WarpPortableSchedulerLayout.Invalid, worker, generation);
        }
        return 0;
    }

    private static uint LeaseOwner(uint[] arena, uint scheduler) => arena[scheduler + WarpPortableSchedulerLayout.ServiceOwner];

    private static uint LeasePending(uint[] arena, uint scheduler) =>
        arena[scheduler + WarpPortableSchedulerLayout.ServicePending] != 0 ? arena[scheduler + WarpPortableSchedulerLayout.ServicePending] :
            arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] == 0 ? 0 : arena[WarpPortableHeapLayout.PendingResult];

    private static uint Terminal(uint state) => state >= WarpPortableSchedulerLayout.Completed && state <= WarpPortableSchedulerLayout.Disposed ? 1u : 0u;

    private static uint ReleasePhysical(uint[] arena, uint scheduler, uint worker)
    {
        uint entry = Worker(arena, scheduler, worker);
        if (arena[entry + WarpPortableSchedulerLayout.WorkerState] != WarpPortableSchedulerLayout.Running)
        {
            return 0;
        }
        uint physical = Physical(arena, scheduler, arena[entry + WarpPortableSchedulerLayout.PhysicalOwner]);
        arena[physical + WarpPortableSchedulerLayout.PhysicalState] = 0;
        arena[physical + WarpPortableSchedulerLayout.PhysicalWorker] = WarpPortableSchedulerLayout.NoWorker;
        arena[entry + WarpPortableSchedulerLayout.PhysicalOwner] = WarpPortableSchedulerLayout.NoWorker;
        arena[entry + WarpPortableSchedulerLayout.QuantumRemaining] = 0;
        arena[scheduler + WarpPortableSchedulerLayout.RunningCount]--;
        return 0;
    }

    private static uint CompleteTerminal(uint[] arena, uint scheduler, uint worker, uint state)
    {
        uint entry = Worker(arena, scheduler, worker);
        uint prior = arena[entry + WarpPortableSchedulerLayout.WorkerState];
        if (Terminal(prior) != 0)
        {
            return 0;
        }
        ReleasePhysical(arena, scheduler, worker);
        if (prior == WarpPortableSchedulerLayout.ParkedGC)
        {
            arena[scheduler + WarpPortableSchedulerLayout.GCParkedCount]--;
        }
        ClearFrameRoots(arena, scheduler, worker);
        if (arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] != 0)
        {
            uint heapWorker = arena[WarpPortableHeapLayout.WorkerStart] + worker * WarpPortableHeapLayout.WorkerWords;
            if (arena[heapWorker] == 2)
            {
                arena[WarpPortableHeapLayout.ParkedWorkers]--;
            }
            if (arena[heapWorker] != 0)
            {
                arena[heapWorker] = 0;
                arena[WarpPortableHeapLayout.ActiveWorkers]--;
            }
        }
        arena[entry + WarpPortableSchedulerLayout.WorkerState] = state;
        arena[entry + WarpPortableSchedulerLayout.SafePoint] = 1;
        arena[scheduler + WarpPortableSchedulerLayout.TerminalCount]++;
        FinishContextState(arena, scheduler);
        return 0;
    }

    private static uint FinishContextState(uint[] arena, uint scheduler)
    {
        if (arena[scheduler + WarpPortableSchedulerLayout.TerminalCount] != arena[scheduler + WarpPortableSchedulerLayout.WorkerCount] ||
            arena[scheduler + WarpPortableSchedulerLayout.RunningCount] != 0 || LeaseOwner(arena, scheduler) != WarpPortableSchedulerLayout.NoWorker)
        {
            return 0;
        }
        if (arena[scheduler + WarpPortableSchedulerLayout.DisposeRequested] != 0)
        {
            arena[scheduler + WarpPortableSchedulerLayout.ContextState] = WarpPortableSchedulerLayout.Disposing;
        }
        else if (arena[scheduler + WarpPortableSchedulerLayout.ContextState] != WarpPortableSchedulerLayout.Quarantined)
        {
            arena[scheduler + WarpPortableSchedulerLayout.ContextState] = arena[scheduler + WarpPortableSchedulerLayout.CancelRequested] != 0 ?
                WarpPortableSchedulerLayout.CancelledContext : WarpPortableSchedulerLayout.CompletedContext;
        }
        return 0;
    }

    private static uint ClearFrameRoots(uint[] arena, uint scheduler, uint worker)
    {
        if (arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] == 0)
        {
            return 0;
        }
        uint entry = Worker(arena, scheduler, worker);
        uint root = arena[entry + WarpPortableSchedulerLayout.FrameRootStart];
        for (uint index = 0; index < arena[entry + WarpPortableSchedulerLayout.FrameRootCapacity]; index++)
        {
            uint reference = arena[WarpPortableHeapLayout.RootStart] + (root + index - 1) * WarpPortableHeapLayout.RootWords + WarpPortableHeapLayout.RootReference;
            arena[reference] = 0;
            arena[reference + 1] = 0;
            arena[reference + 2] = 0;
        }
        return 0;
    }
}
