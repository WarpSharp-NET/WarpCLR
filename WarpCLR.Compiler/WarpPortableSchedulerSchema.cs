using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace WarpCLR.Compiler;

internal sealed class WarpPortableSchedulerSchema
{
    private readonly uint[] metadata;

    public WarpPortableSchedulerSchema(uint workerCount, uint residentCount, uint quantum,
        uint stackLimit, uint stepBudgetLow, uint stepBudgetHigh, uint logicalStateWords,
        IEnumerable<WarpPortableSchedulerRootLayout> rootMaps, IEnumerable<WarpPortableSchedulerBarrierLayout> barriers,
        IEnumerable<uint>? outputReferenceOffsets = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(workerCount);
        ArgumentOutOfRangeException.ThrowIfZero(residentCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(residentCount, workerCount);
        ArgumentOutOfRangeException.ThrowIfZero(quantum);
        ArgumentOutOfRangeException.ThrowIfZero(stackLimit);
        ArgumentOutOfRangeException.ThrowIfZero(logicalStateWords);
        ArgumentNullException.ThrowIfNull(rootMaps);
        ArgumentNullException.ThrowIfNull(barriers);
        WarpPortableSchedulerRootLayout[] maps = rootMaps.ToArray();
        WarpPortableSchedulerBarrierLayout[] collectives = barriers.ToArray();
        uint[] outputs = outputReferenceOffsets?.ToArray() ?? [];
        ArgumentOutOfRangeException.ThrowIfZero(maps.Length, nameof(rootMaps));
        ValidateMaps(maps, logicalStateWords);
        ValidateMaps([new(0, 0, outputs)], logicalStateWords);
        ValidateBarriers(collectives, workerCount);
        metadata = CreateMetadata(workerCount, residentCount, quantum, stackLimit,
            stepBudgetLow, stepBudgetHigh, logicalStateWords, maps, collectives, outputs);
    }

    public uint[] CreateArena(uint context)
    {
        ArgumentOutOfRangeException.ThrowIfZero(context);
        uint[] arena = (uint[])metadata.Clone();
        Bind(arena, 0, context, heapLinked: false);
        WriteBoundIdentity(arena, 0, heapLinked: false);
        return arena;
    }

    public uint[] AttachToEmptyHeap(uint[] heap)
    {
        ArgumentNullException.ThrowIfNull(heap);
        if (heap.Length < WarpPortableHeapLayout.HeaderWords || heap[0] != WarpPortableHeapLayout.Magic ||
            heap[1] != WarpPortableHeapLayout.Version || heap[WarpPortableHeapLayout.Context] == 0 ||
            heap[WarpPortableSchedulerLayout.HeapDescriptor] != 0 ||
            heap[WarpPortableHeapLayout.UsedWords] != 0 || heap[WarpPortableHeapLayout.LiveObjects] != 0 ||
            heap[WarpPortableHeapLayout.LiveRoots] != 0 || heap[WarpPortableHeapLayout.ActiveWorkers] != 0 ||
            heap[WarpPortableHeapLayout.LeaseState] != 0 || heap[WarpPortableHeapLayout.PendingResult] != 0 ||
            heap[WarpPortableHeapLayout.CollectionState] != WarpPortableHeapLayout.Idle)
        {
            throw new ArgumentException("A scheduler can attach only to an unused, idle heap template.", nameof(heap));
        }
        uint workers = metadata[WarpPortableSchedulerLayout.WorkerCount];
        uint roots = checked(workers * metadata[WarpPortableSchedulerLayout.RootSlotsPerWorker]);
        if (heap[WarpPortableHeapLayout.WorkerCount] != workers || heap[WarpPortableHeapLayout.RootCount] < roots)
        {
            throw new ArgumentException("Heap worker/root admission must cover every scheduler participant and reserved root.", nameof(heap));
        }
        ValidateRootReservation(heap, roots);
        uint scheduler = heap[WarpPortableHeapLayout.DataStart];
        uint[] arena = new uint[checked(heap.Length + metadata.Length)];
        heap.AsSpan(0, checked((int)scheduler)).CopyTo(arena);
        metadata.CopyTo(arena, checked((int)scheduler));
        heap.AsSpan(checked((int)scheduler)).CopyTo(arena.AsSpan(checked((int)scheduler + metadata.Length)));
        arena[3] = (uint)arena.Length;
        arena[WarpPortableHeapLayout.DataStart] += (uint)metadata.Length;
        arena[WarpPortableSchedulerLayout.HeapDescriptor] = scheduler;
        Bind(arena, scheduler, heap[WarpPortableHeapLayout.Context], heapLinked: true);
        ReserveHeapParticipants(arena, scheduler);
        WriteBoundIdentity(arena, scheduler, heapLinked: true);
        return arena;
    }

    private static void ValidateRootReservation(uint[] heap, uint count)
    {
        for (uint root = 0; root < count; root++)
        {
            uint entry = checked(heap[WarpPortableHeapLayout.RootStart] + root * WarpPortableHeapLayout.RootWords);
            if (entry > heap.Length - WarpPortableHeapLayout.RootWords ||
                heap[entry + WarpPortableHeapLayout.RootState] != WarpPortableHeapLayout.Free ||
                heap[entry + WarpPortableHeapLayout.RootGeneration] != 1 ||
                heap[entry + WarpPortableHeapLayout.RootOwnership] != WarpPortableHeapLayout.UnownedRoot)
            {
                throw new ArgumentException("Scheduler root reservation requires pristine unowned rows and cannot repurpose host or runtime roots.", nameof(heap));
            }
        }
    }

    private static void ValidateMaps(WarpPortableSchedulerRootLayout[] maps, uint stateWords)
    {
        foreach (WarpPortableSchedulerRootLayout map in maps)
        {
            uint previousEnd = 0;
            foreach (uint offset in map.ReferenceOffsets)
            {
                if (stateWords < 3 || offset > stateWords - 3 || offset < previousEnd)
                {
                    throw new ArgumentException("Root tuple offsets must be sorted, disjoint, and inside the admitted worker state.", nameof(maps));
                }
                previousEnd = offset + 3;
            }
        }
    }

    private static void ValidateBarriers(WarpPortableSchedulerBarrierLayout[] barriers, uint workers)
    {
        foreach (WarpPortableSchedulerBarrierLayout barrier in barriers)
        {
            var membership = new HashSet<uint>();
            foreach (uint worker in barrier.Members)
            {
                if (worker >= workers || !membership.Add(worker))
                {
                    throw new ArgumentException("Collective membership must contain distinct admitted workers.", nameof(barriers));
                }
            }
            if (barrier.Scope == WarpPortableSchedulerLayout.GridScope && (uint)membership.Count != workers)
            {
                throw new ArgumentException("A grid collective must include every admitted logical worker.", nameof(barriers));
            }
        }
    }

    private static uint[] CreateMetadata(uint workers, uint residents, uint quantum, uint stackLimit,
        uint stepLow, uint stepHigh, uint stateWords, WarpPortableSchedulerRootLayout[] maps,
        WarpPortableSchedulerBarrierLayout[] barriers, uint[] outputOffsets)
    {
        uint physical = checked(WarpPortableSchedulerLayout.HeaderWords + workers * WarpPortableSchedulerLayout.WorkerWords);
        uint barrierStart = checked(physical + residents * WarpPortableSchedulerLayout.PhysicalWords);
        uint membership = checked(barrierStart + (uint)barriers.Length * WarpPortableSchedulerLayout.BarrierWords);
        uint rootMaps = checked(membership + (uint)barriers.Length * workers);
        uint offsets = checked(rootMaps + (uint)maps.Length * WarpPortableSchedulerLayout.RootMapWords);
        uint referenceCount = 0;
        uint capacity = 0;
        foreach (WarpPortableSchedulerRootLayout map in maps)
        {
            referenceCount = checked(referenceCount + (uint)map.ReferenceOffsets.Count);
            capacity = Math.Max(capacity, (uint)map.ReferenceOffsets.Count);
        }
        uint outputStart = checked(offsets + referenceCount);
        uint state = checked(outputStart + (uint)outputOffsets.Length);
        uint length = checked(state + workers * stateWords);
        uint[] words = new uint[checked((int)length)];
        SetHeader(words, workers, residents, quantum, stateWords, barriers.Length, maps.Length,
            physical, barrierStart, membership, rootMaps, offsets, state, capacity, length);
        for (uint worker = 0; worker < workers; worker++)
        {
            uint entry = WarpPortableSchedulerLayout.HeaderWords + worker * WarpPortableSchedulerLayout.WorkerWords;
            words[entry + WarpPortableSchedulerLayout.WorkerState] = WarpPortableSchedulerLayout.Ready;
            words[entry + WarpPortableSchedulerLayout.PhysicalOwner] = WarpPortableSchedulerLayout.NoWorker;
            words[entry + WarpPortableSchedulerLayout.StepRemainingLow] = stepLow;
            words[entry + WarpPortableSchedulerLayout.StepRemainingHigh] = stepHigh;
            words[entry + WarpPortableSchedulerLayout.UserStackDepth] = 1;
            words[entry + WarpPortableSchedulerLayout.UserStackLimit] = stackLimit;
            words[entry + WarpPortableSchedulerLayout.InitialStackLimit] = stackLimit;
            words[entry + WarpPortableSchedulerLayout.InitialStepLow] = stepLow;
            words[entry + WarpPortableSchedulerLayout.InitialStepHigh] = stepHigh;
            words[entry + WarpPortableSchedulerLayout.LogicalStackBase] = state + worker * stateWords;
            words[entry + WarpPortableSchedulerLayout.SafePoint] = 1;
            words[entry + WarpPortableSchedulerLayout.FrameRootCapacity] = capacity;
        }
        WriteBarriers(words, barriers, barrierStart, membership, workers);
        WriteMaps(words, maps, rootMaps, offsets);
        words[WarpPortableSchedulerLayout.OutputReferenceCount] = (uint)outputOffsets.Length;
        words[WarpPortableSchedulerLayout.OutputOffsets] = outputStart;
        words[WarpPortableSchedulerLayout.DispatchGeneration] = 1;
        words[WarpPortableSchedulerLayout.RootSlotsPerWorker] += (uint)outputOffsets.Length;
        outputOffsets.CopyTo(words, checked((int)outputStart));
        WriteIdentity(words);
        return words;
    }

    private static void SetHeader(uint[] words, uint workers, uint residents, uint quantum, uint stateWords,
        int barriers, int maps, uint physical, uint barrierStart, uint membership, uint rootMaps,
        uint offsets, uint state, uint rootCapacity, uint length)
    {
        words[0] = WarpPortableSchedulerLayout.Magic;
        words[1] = WarpPortableSchedulerLayout.Version;
        words[WarpPortableSchedulerLayout.ContextState] = WarpPortableSchedulerLayout.Active;
        words[WarpPortableSchedulerLayout.WorkerCount] = workers;
        words[WarpPortableSchedulerLayout.WorkerStart] = WarpPortableSchedulerLayout.HeaderWords;
        words[WarpPortableSchedulerLayout.ResidentCount] = residents;
        words[WarpPortableSchedulerLayout.PhysicalStart] = physical;
        words[WarpPortableSchedulerLayout.Quantum] = quantum;
        words[WarpPortableSchedulerLayout.BarrierCount] = (uint)barriers;
        words[WarpPortableSchedulerLayout.BarrierStart] = barrierStart;
        words[WarpPortableSchedulerLayout.MembershipStart] = membership;
        words[WarpPortableSchedulerLayout.RootMapCount] = (uint)maps;
        words[WarpPortableSchedulerLayout.ServiceOwner] = WarpPortableSchedulerLayout.NoWorker;
        words[WarpPortableSchedulerLayout.FaultWinner] = WarpPortableSchedulerLayout.NoWorker;
        words[WarpPortableSchedulerLayout.RootWordQuota] = stateWords;
        words[WarpPortableSchedulerLayout.RootMapStart] = rootMaps;
        words[WarpPortableSchedulerLayout.RootOffsetStart] = offsets;
        words[WarpPortableSchedulerLayout.LogicalStateStart] = state;
        words[WarpPortableSchedulerLayout.LogicalStateWords] = stateWords;
        words[WarpPortableSchedulerLayout.RootSlotsPerWorker] = checked(rootCapacity + 1);
        words[WarpPortableSchedulerLayout.DescriptorWords] = length;
    }

    private static void WriteBarriers(uint[] words, WarpPortableSchedulerBarrierLayout[] barriers,
        uint barrierStart, uint membership, uint workers)
    {
        for (int index = 0; index < barriers.Length; index++)
        {
            WarpPortableSchedulerBarrierLayout barrier = barriers[index];
            uint entry = barrierStart + (uint)index * WarpPortableSchedulerLayout.BarrierWords;
            uint members = membership + (uint)index * workers;
            words[entry + WarpPortableSchedulerLayout.BarrierGeneration] = 1;
            words[entry + WarpPortableSchedulerLayout.BarrierExpected] = (uint)barrier.Members.Count;
            words[entry + WarpPortableSchedulerLayout.BarrierSite] = barrier.Site;
            words[entry + WarpPortableSchedulerLayout.BarrierMembership] = members;
            words[entry + WarpPortableSchedulerLayout.BarrierScope] = barrier.Scope;
            foreach (uint worker in barrier.Members)
            {
                words[members + worker] = 1;
            }
        }
    }

    private static void WriteMaps(uint[] words, WarpPortableSchedulerRootLayout[] maps, uint mapStart, uint offsetStart)
    {
        uint next = offsetStart;
        for (int index = 0; index < maps.Length; index++)
        {
            WarpPortableSchedulerRootLayout map = maps[index];
            uint entry = mapStart + (uint)index * WarpPortableSchedulerLayout.RootMapWords;
            words[entry + WarpPortableSchedulerLayout.MapReferenceCount] = (uint)map.ReferenceOffsets.Count;
            words[entry + WarpPortableSchedulerLayout.MapOffsets] = next;
            words[entry + WarpPortableSchedulerLayout.MapFunction] = map.Function;
            words[entry + WarpPortableSchedulerLayout.MapPC] = map.PC;
            foreach (uint offset in map.ReferenceOffsets)
            {
                words[next++] = offset;
            }
        }
    }

    private static void WriteIdentity(uint[] words)
    {
        byte[] semantics = Encoding.UTF8.GetBytes(WarpPortableSchedulerLayout.Semantics);
        byte[] canonical = new byte[checked(semantics.Length + words.Length * sizeof(uint))];
        semantics.CopyTo(canonical, 0);
        for (int index = 0; index < words.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(canonical.AsSpan(semantics.Length + index * sizeof(uint)), words[index]);
        }
        byte[] hash = SHA256.HashData(canonical);
        for (int index = 0; index < hash.Length / sizeof(uint); index++)
        {
            words[WarpPortableSchedulerLayout.SchemaHash + index] = BinaryPrimitives.ReadUInt32LittleEndian(hash.AsSpan(index * sizeof(uint)));
        }
    }

    private static void Bind(uint[] arena, uint scheduler, uint context, bool heapLinked)
    {
        arena[scheduler + WarpPortableSchedulerLayout.Context] = context;
        arena[scheduler + WarpPortableSchedulerLayout.ArenaWords] = (uint)arena.Length;
        arena[scheduler + WarpPortableSchedulerLayout.HeapLinked] = heapLinked ? 1u : 0u;
        uint workers = arena[scheduler + WarpPortableSchedulerLayout.WorkerCount];
        for (uint worker = 0; worker < workers; worker++)
        {
            uint entry = scheduler + arena[scheduler + WarpPortableSchedulerLayout.WorkerStart] + worker * WarpPortableSchedulerLayout.WorkerWords;
            arena[entry + WarpPortableSchedulerLayout.LogicalStackBase] += scheduler;
        }
    }

    private static void WriteBoundIdentity(uint[] arena, uint scheduler, bool heapLinked)
    {
        uint[] identity = new uint[34];
        for (uint word = 0; word < 8; word++)
        {
            identity[word] = arena[scheduler + WarpPortableSchedulerLayout.SchemaHash + word];
            identity[word + 8] = heapLinked ? arena[WarpPortableHeapLayout.SchemaHash + word] : 0;
        }
        identity[16] = scheduler;
        identity[17] = WarpPortableSchedulerLayout.HeapDescriptor;
        identity[18] = WarpPortableSchedulerLayout.Version;
        identity[19] = heapLinked ? 1u : 0u;
        identity[20] = arena[scheduler + WarpPortableSchedulerLayout.DescriptorWords];
        identity[21] = arena[scheduler + WarpPortableSchedulerLayout.ArenaWords];
        if (heapLinked)
        {
            identity[22] = arena[WarpPortableHeapLayout.RootStart];
            identity[23] = arena[WarpPortableHeapLayout.RootCount];
            identity[24] = arena[WarpPortableHeapLayout.WorkerStart];
            identity[25] = arena[WarpPortableHeapLayout.WorkerCount];
            identity[26] = arena[WarpPortableHeapLayout.DataStart];
            identity[27] = arena[WarpPortableHeapLayout.ScratchStart];
            identity[28] = arena[WarpPortableHeapLayout.ScratchWords];
            identity[29] = arena[scheduler + WarpPortableSchedulerLayout.RootSlotsPerWorker];
        }
        identity[30] = WarpPortableHeapLayout.RootOwnership;
        identity[31] = WarpPortableHeapLayout.UnownedRoot;
        identity[32] = WarpPortableHeapLayout.HostOwnedRoot;
        identity[33] = WarpPortableHeapLayout.RuntimeOwnedRoot;
        byte[] bytes = new byte[identity.Length * sizeof(uint)];
        for (int word = 0; word < identity.Length; word++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(word * sizeof(uint)), identity[word]);
        }
        byte[] hash = SHA256.HashData(bytes);
        for (int word = 0; word < hash.Length / sizeof(uint); word++)
        {
            arena[scheduler + WarpPortableSchedulerLayout.SchemaHash + word] = BinaryPrimitives.ReadUInt32LittleEndian(hash.AsSpan(word * sizeof(uint)));
        }
    }

    private static void ReserveHeapParticipants(uint[] arena, uint scheduler)
    {
        uint workers = arena[scheduler + WarpPortableSchedulerLayout.WorkerCount];
        uint capacity = arena[scheduler + WarpPortableSchedulerLayout.RootSlotsPerWorker];
        for (uint worker = 0; worker < workers; worker++)
        {
            uint entry = scheduler + arena[scheduler + WarpPortableSchedulerLayout.WorkerStart] + worker * WarpPortableSchedulerLayout.WorkerWords;
            arena[entry + WarpPortableSchedulerLayout.FrameRootStart] = worker * capacity + 1;
            arena[arena[WarpPortableHeapLayout.WorkerStart] + worker * WarpPortableHeapLayout.WorkerWords] = 1;
        }
        for (uint root = 0; root < workers * capacity; root++)
        {
            uint entry = arena[WarpPortableHeapLayout.RootStart] + root * WarpPortableHeapLayout.RootWords;
            arena[entry + WarpPortableHeapLayout.RootState] = WarpPortableHeapLayout.Allocated;
            arena[entry + WarpPortableHeapLayout.RootKind] = WarpPortableHeapLayout.StrongRoot;
            arena[entry + WarpPortableHeapLayout.RootOwnership] = WarpPortableHeapLayout.RuntimeOwnedRoot;
        }
        arena[WarpPortableHeapLayout.ActiveWorkers] = workers;
        arena[WarpPortableHeapLayout.LiveRoots] = workers * capacity;
    }
}
