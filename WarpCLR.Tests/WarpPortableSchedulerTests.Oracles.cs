using WarpCLR.Compiler;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableSchedulerTests
{
    [TestMethod]
    public void AtomicClaimantsMatchTheAllowedOutcomeSetAndFailedCASYieldsOnce()
    {
        const uint contenders = 8;
        var observed = new HashSet<uint>();
        var allowed = Enumerable.Range(1, (int)contenders).Select(static value => (uint)value).ToHashSet();
        for (uint first = 1; first <= contenders; first++)
        {
            uint[] arena = Schema(23, contenders, 1).CreateArena(300 + first);
            uint attempts = 0;
            uint successes = 0;
            for (uint offset = 0; offset < contenders; offset++)
            {
                uint token = (first + offset - 1) % contenders + 1;
                uint previous = Interlocked.CompareExchange(ref arena[WarpPortableSchedulerLayout.ControllerOwner], token, 0);
                attempts++;
                if (previous == 0)
                {
                    observed.Add(token);
                    successes++;
                    Success(WarpPortableSchedulerServices.TryAcquireWorker(arena, 0, token, token - 1));
                    Assert.AreEqual(0u, arena[WarpPortableSchedulerLayout.Result]);
                }
                else
                {
                    uint[] before = (uint[])arena.Clone();
                    Assert.AreEqual(WarpPortableSchedulerLayout.Yield, WarpPortableSchedulerServices.TryAcquireWorker(arena, 0, token, token - 1));
                    CollectionAssert.AreEqual(before, arena);
                }
            }
            Assert.AreEqual(contenders, attempts);
            Assert.AreEqual(1u, successes);
            Assert.AreEqual(1u, arena[WarpPortableSchedulerLayout.RunningCount]);
            Assert.AreEqual(first, Interlocked.CompareExchange(ref arena[WarpPortableSchedulerLayout.ControllerOwner], 0, first));
        }
        Assert.IsTrue(observed.SetEquals(allowed), "Every arbitration-dependent claimant must remain permitted; no universal physical winner is fabricated.");
    }

    [TestMethod]
    public async Task ConcurrentAtomicClaimsHaveOneControllerAndLeaveEveryLogicalFrameIntact()
    {
        const uint contenders = 8;
        uint[] arena = Schema(73, contenders, 1).CreateArena(312);
        int attempts = 0;
        int successes = 0;
        int winner = 0;
        Task[] tasks = Enumerable.Range(1, (int)contenders).Select(token => Task.Run(() =>
        {
            Interlocked.Increment(ref attempts);
            uint previous = Interlocked.CompareExchange(ref arena[WarpPortableSchedulerLayout.ControllerOwner], (uint)token, 0);
            if (previous == 0)
            {
                Interlocked.Increment(ref successes);
                Volatile.Write(ref winner, token);
                Success(WarpPortableSchedulerServices.TryAcquireWorker(arena, 0, (uint)token, (uint)token - 1));
            }
            else
            {
                Assert.AreEqual(WarpPortableSchedulerLayout.Yield, WarpPortableSchedulerServices.TryAcquireWorker(arena, 0, (uint)token, (uint)token - 1));
            }
        })).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        Assert.AreEqual((int)contenders, attempts);
        Assert.AreEqual(1, successes);
        Assert.IsTrue(winner is >= 1 and <= (int)contenders);
        Assert.AreEqual(1u, arena[WarpPortableSchedulerLayout.RunningCount]);
        for (uint worker = 1; worker < 73; worker++)
        {
            Assert.AreEqual(WarpPortableSchedulerLayout.Ready, arena[Entry(arena, 0, worker) + WarpPortableSchedulerLayout.WorkerState]);
        }
        Assert.AreEqual((uint)winner, Interlocked.CompareExchange(ref arena[WarpPortableSchedulerLayout.ControllerOwner], 0, (uint)winner));
    }

    [TestMethod]
    public void RandomOversubscribedBarrierGCInterleavingsMatchClosedFormAtBothQuanta()
    {
        for (uint scenario = 1; scenario <= 64; scenario++)
        {
            uint[] small = RunInterleaving(scenario, 1);
            uint[] large = RunInterleaving(scenario, 17);
            CollectionAssert.AreEqual(small, large);
            for (uint worker = 0; worker < (uint)small.Length; worker++)
            {
                uint expected = unchecked((worker + 1) * 15 * 0x9E3779B9u);
                Assert.AreEqual(expected, small[worker]);
            }
        }
    }

    [TestMethod]
    public void SchemaIdentityBindsPreciseRootMapsCollectiveMembershipAndHeapDescriptor()
    {
        WarpPortableSchedulerSchema schema = Schema(3, 1, 1, barriers: [Grid(3, 17)]);
        uint[] first = schema.CreateArena(401);
        uint[] second = schema.CreateArena(402);
        CollectionAssert.AreEqual(Hash(first, 0), Hash(second, 0), "Context identity is separate from immutable schema identity.");
        WarpPortableSchedulerSchema differentMap = new(3, 1, 1, 4, 4096, 0, 16,
            [new(0, 0, []), new(4, 22, [3])], [Grid(3, 17)]);
        CollectionAssert.AreNotEqual(Hash(first, 0), Hash(differentMap.CreateArena(401), 0));
        WarpPortableSchedulerSchema differentMembers = Schema(3, 1, 1,
            barriers: [new(17, WarpPortableSchedulerLayout.GroupScope, [0, 2])]);
        CollectionAssert.AreNotEqual(Hash(first, 0), Hash(differentMembers.CreateArena(401), 0));
        uint[] heap = HeapArena(3, 1, 1, [Grid(3, 17)]);
        uint descriptor = heap[WarpPortableSchedulerLayout.HeapDescriptor];
        Assert.IsGreaterThanOrEqualTo(WarpPortableHeapLayout.HeaderWords, descriptor);
        Assert.IsLessThan(heap[WarpPortableHeapLayout.DataStart], descriptor);
        CollectionAssert.AreNotEqual(Hash(first, 0), Hash(heap, descriptor));
        Assert.AreEqual(WarpPortableSchedulerLayout.Magic, heap[descriptor]);
        Assert.AreEqual((uint)heap.Length, heap[descriptor + WarpPortableSchedulerLayout.ArenaWords]);
        Assert.AreEqual((uint)heap.Length - heap[WarpPortableHeapLayout.DataStart], heap[heap[WarpPortableHeapLayout.DataStart] + WarpPortableHeapLayout.BlockSize]);
        Assert.ThrowsExactly<ArgumentException>(() => schema.AttachToEmptyHeap(heap));
        Assert.ThrowsExactly<ArgumentException>(() => new WarpPortableSchedulerSchema(3, 1, 1, 4, 1, 0, 16,
            [new(0, 0, [0, 2])], []));
        Assert.ThrowsExactly<ArgumentException>(() => Schema(3, 1, 1,
            barriers: [new(17, WarpPortableSchedulerLayout.GridScope, [0, 1])]));
    }

    private static uint[] RunInterleaving(uint scenario, uint quantum)
    {
        uint seed = scenario * 0x9E3779B9u;
        uint workers = 19 + Next(ref seed) % 79;
        uint residents = 1 + Next(ref seed) % 3;
        uint[] arena = HeapArena(workers, residents, quantum, [Grid(workers, 17)]);
        uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
        Claim(arena, scheduler);
        uint[] output = new uint[workers];
        for (uint phase = 1; phase <= 5; phase++)
        {
            for (uint arrived = 0; arrived < workers; arrived++)
            {
                if (arrived == workers / 2 || (Next(ref seed) & 15) == 0)
                {
                    Success(WarpPortableSchedulerServices.RequestCollection(arena, scheduler, Controller));
                    uint epoch = arena[scheduler + WarpPortableSchedulerLayout.GCEpoch];
                    for (uint worker = 0; worker < workers; worker++)
                    {
                        PublishEmpty(arena, scheduler, worker, epoch);
                        uint run = arena[Entry(arena, scheduler, worker) + WarpPortableSchedulerLayout.RunGeneration];
                        Success(WarpPortableSchedulerServices.ParkForCollection(arena, scheduler, Controller, worker, run, epoch));
                    }
                    Collect(arena, scheduler, epoch);
                }
                (uint selected, uint generation) = Acquire(arena, scheduler, Next(ref seed) % residents);
                Success(WarpPortableSchedulerServices.ChargeQuantum(arena, scheduler, Controller, selected, generation, quantum));
                Success(WarpPortableSchedulerServices.ChargeUserSteps(arena, scheduler, Controller, selected, generation, 1));
                output[selected] += unchecked((selected + 1) * phase * 0x9E3779B9u);
                Success(WarpPortableSchedulerServices.ArriveBarrier(arena, scheduler, Controller, selected, generation, 0, phase, 17));
            }
            Assert.AreEqual(phase + 1, arena[Collective(arena, scheduler, 0) + WarpPortableSchedulerLayout.BarrierGeneration]);
        }
        for (uint completed = 0; completed < workers; completed++)
        {
            (uint worker, uint generation) = Acquire(arena, scheduler, completed % residents);
            Assert.AreEqual(5u, arena[Entry(arena, scheduler, worker) + WarpPortableSchedulerLayout.StepSpentLow]);
            Success(WarpPortableSchedulerServices.CompleteWorker(arena, scheduler, Controller, worker, generation));
        }
        Assert.AreEqual(WarpPortableSchedulerLayout.CompletedContext, arena[scheduler + WarpPortableSchedulerLayout.ContextState]);
        Assert.AreEqual(0u, arena[scheduler + WarpPortableSchedulerLayout.OutputQuarantined]);
        Assert.IsGreaterThanOrEqualTo(5u, arena[scheduler + WarpPortableSchedulerLayout.GCEpoch]);
        return output;
    }

    private static uint[] Hash(uint[] arena, uint scheduler) => arena.AsSpan((int)(scheduler + WarpPortableSchedulerLayout.SchemaHash), 8).ToArray();

    private static uint Next(ref uint seed)
    {
        seed ^= seed << 13;
        seed ^= seed >> 17;
        seed ^= seed << 5;
        return seed;
    }
}
