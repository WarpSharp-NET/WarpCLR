using WarpCLR.Compiler;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableSchedulerTests
{
    [TestMethod]
    public void GuessedRuntimeRootReadAndReleaseFailBeforeAnyReservedRowMutation()
    {
        uint[] arena = HeapArena(1, 1, 1, outputs: [0]);
        uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
        Claim(arena, scheduler);
        Success(WarpPortableHeapServices.AllocateObject(arena, 1));
        WarpPortableHeapReference value = HeapResult(arena);
        CompleteReferenceDispatch(arena, scheduler, value);
        uint[] reserved = ReservedRows(arena, scheduler);
        for (uint root = 1; root <= arena[scheduler + WarpPortableSchedulerLayout.RootSlotsPerWorker]; root++)
        {
            uint entry = RootEntry(arena, root);
            uint generation = arena[entry + WarpPortableHeapLayout.RootGeneration];
            Assert.AreEqual(WarpPortableHeapLayout.RuntimeOwnedRoot, arena[entry + WarpPortableHeapLayout.RootOwnership]);
            Assert.AreEqual(WarpPortableHeapLayout.InvalidReference, WarpPortableHeapServices.ReadRoot(arena, root, generation));
            CollectionAssert.AreEqual(reserved, ReservedRows(arena, scheduler));
            Assert.AreEqual(WarpPortableHeapLayout.InvalidReference, WarpPortableHeapServices.ReleaseRoot(arena, root, generation));
            CollectionAssert.AreEqual(reserved, ReservedRows(arena, scheduler));
        }
        Success(WarpPortableSchedulerServices.RequestCollection(arena, scheduler, Controller));
        Collect(arena, scheduler, 1);
        Assert.AreEqual(1u, arena[WarpPortableHeapLayout.LiveObjects]);
        Success(WarpPortableSchedulerServices.ReleaseOutputRoots(arena, scheduler, Controller, 0, 1));
        Success(WarpPortableSchedulerServices.RequestCollection(arena, scheduler, Controller));
        Collect(arena, scheduler, 2);
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.LiveObjects]);
    }

    [TestMethod]
    public void HostAcquireExhaustsOnlyUnownedRowsAndNormalHandlesRetainTheirLifecycle()
    {
        uint[] arena = HeapArena(1, 1, 1, outputs: [0]);
        uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
        uint reservedCount = arena[scheduler + WarpPortableSchedulerLayout.RootSlotsPerWorker];
        uint[] reserved = ReservedRows(arena, scheduler);
        var acquired = new List<(uint Root, uint Generation)>();
        for (uint index = reservedCount; index < arena[WarpPortableHeapLayout.RootCount]; index++)
        {
            Success(WarpPortableHeapServices.AcquireRoot(arena, 0, 0, 0, WarpPortableHeapLayout.StrongRoot, 0, 0, 0));
            uint root = arena[WarpPortableHeapLayout.Result];
            Assert.IsGreaterThan(reservedCount, root);
            Assert.AreEqual(WarpPortableHeapLayout.HostOwnedRoot, arena[RootEntry(arena, root) + WarpPortableHeapLayout.RootOwnership]);
            acquired.Add((root, arena[WarpPortableHeapLayout.Result + 1]));
        }
        Assert.AreEqual(WarpPortableHeapLayout.Quota, WarpPortableHeapServices.AcquireRoot(arena, 0, 0, 0, WarpPortableHeapLayout.StrongRoot, 0, 0, 0));
        CollectionAssert.AreEqual(reserved, ReservedRows(arena, scheduler));
        foreach ((uint root, uint generation) in acquired)
        {
            Success(WarpPortableHeapServices.ReadRoot(arena, root, generation));
            Success(WarpPortableHeapServices.ReleaseRoot(arena, root, generation));
            Assert.AreEqual(WarpPortableHeapLayout.UnownedRoot, arena[RootEntry(arena, root) + WarpPortableHeapLayout.RootOwnership]);
            Assert.AreEqual(WarpPortableHeapLayout.InvalidReference, WarpPortableHeapServices.ReadRoot(arena, root, generation));
        }
        Success(WarpPortableHeapServices.AcquireRoot(arena, 0, 0, 0, WarpPortableHeapLayout.StrongRoot, 0, 0, 0));
        Assert.AreEqual(acquired[0].Root, arena[WarpPortableHeapLayout.Result]);
        Assert.AreEqual(acquired[0].Generation + 1, arena[WarpPortableHeapLayout.Result + 1]);
        CollectionAssert.AreEqual(reserved, ReservedRows(arena, scheduler));
    }

    [TestMethod]
    public void SchedulerRejectsLostOwnershipBeforePublicationAndDoesNotRepurposeHostRows()
    {
        uint[] arena = HeapArena(1, 1, 1, outputs: [0]);
        uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
        Claim(arena, scheduler);
        (uint worker, uint generation) = Acquire(arena, scheduler, 0);
        uint root = RootEntry(arena, 1);
        arena[root + WarpPortableHeapLayout.RootOwnership] = WarpPortableHeapLayout.HostOwnedRoot;
        uint[] row = arena.AsSpan((int)root, (int)WarpPortableHeapLayout.RootWords).ToArray();
        Assert.AreEqual(WarpPortableSchedulerLayout.Invalid, WarpPortableSchedulerServices.PublishRoots(arena, scheduler, Controller, worker, generation, 0, 0, 1, 0, 0));
        Assert.AreEqual(WarpPortableSchedulerLayout.Invalid, WarpPortableSchedulerServices.PublishOutputRoots(arena, scheduler, Controller, worker, generation, 1));
        Assert.AreEqual(WarpPortableSchedulerLayout.Invalid, WarpPortableSchedulerServices.RequestDisposal(arena, scheduler, Controller));
        CollectionAssert.AreEqual(row, arena.AsSpan((int)root, (int)WarpPortableHeapLayout.RootWords).ToArray());
        arena[root + WarpPortableHeapLayout.RootOwnership] = WarpPortableHeapLayout.RuntimeOwnedRoot;
        arena[root + WarpPortableHeapLayout.RootState] = WarpPortableHeapLayout.Free;
        Success(WarpPortableHeapServices.AcquireRoot(arena, 0, 0, 0, WarpPortableHeapLayout.StrongRoot, 0, 0, 0));
        Assert.IsGreaterThan(arena[scheduler + WarpPortableSchedulerLayout.RootSlotsPerWorker], arena[WarpPortableHeapLayout.Result]);
        Assert.AreEqual(WarpPortableHeapLayout.RuntimeOwnedRoot, arena[root + WarpPortableHeapLayout.RootOwnership]);
    }

    [TestMethod]
    public void DisposalClearsRuntimeTuplesAndPreservesOwnershipAndHostRootContents()
    {
        uint[] arena = HeapArena(1, 1, 1, outputs: [0]);
        uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
        Claim(arena, scheduler);
        Success(WarpPortableHeapServices.AllocateObject(arena, 1));
        WarpPortableHeapReference value = HeapResult(arena);
        Success(WarpPortableHeapServices.AcquireRoot(arena, value.Context, value.Slot, value.Generation, WarpPortableHeapLayout.StrongRoot, 0, 0, 0));
        uint host = arena[WarpPortableHeapLayout.Result];
        uint hostGeneration = arena[WarpPortableHeapLayout.Result + 1];
        uint hostEntry = RootEntry(arena, host);
        uint[] hostRow = arena.AsSpan((int)hostEntry, (int)WarpPortableHeapLayout.RootWords).ToArray();
        CompleteReferenceDispatch(arena, scheduler, value);
        Success(WarpPortableSchedulerServices.RequestDisposal(arena, scheduler, Controller));
        Success(WarpPortableSchedulerServices.FinishDisposal(arena, scheduler, Controller));
        CollectionAssert.AreEqual(hostRow, arena.AsSpan((int)hostEntry, (int)WarpPortableHeapLayout.RootWords).ToArray());
        for (uint root = 1; root <= arena[scheduler + WarpPortableSchedulerLayout.RootSlotsPerWorker]; root++)
        {
            uint entry = RootEntry(arena, root);
            Assert.AreEqual(WarpPortableHeapLayout.RuntimeOwnedRoot, arena[entry + WarpPortableHeapLayout.RootOwnership]);
            Assert.AreEqual(WarpPortableHeapLayout.Allocated, arena[entry + WarpPortableHeapLayout.RootState]);
            Assert.AreEqual(0u, arena[entry + WarpPortableHeapLayout.RootReference + 1]);
            Assert.AreEqual(WarpPortableHeapLayout.InvalidReference, WarpPortableHeapServices.ReleaseRoot(arena, root, arena[entry + WarpPortableHeapLayout.RootGeneration]));
        }
        Success(WarpPortableHeapServices.RequestCollection(arena));
        Success(WarpPortableHeapServices.Collect(arena));
        Assert.AreEqual(1u, arena[WarpPortableHeapLayout.LiveObjects]);
        Success(WarpPortableHeapServices.ReadRoot(arena, host, hostGeneration));
        Assert.AreEqual(value, HeapResult(arena));
        Success(WarpPortableHeapServices.ReleaseRoot(arena, host, hostGeneration));
        Success(WarpPortableHeapServices.RequestCollection(arena));
        Success(WarpPortableHeapServices.Collect(arena));
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.LiveObjects]);
    }

    [TestMethod]
    public void OwnershipV2RejectsVersionOneArenaAndReservationOverHostHistory()
    {
        uint[] arena = HeapArena(1, 1, 1, outputs: [0]);
        uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
        Claim(arena, scheduler);
        Assert.AreEqual(2u, arena[1]);
        Assert.AreEqual(2u, arena[scheduler + 1]);
        arena[1] = 1;
        Assert.AreEqual(WarpPortableSchedulerLayout.Invalid, WarpPortableSchedulerServices.TryAcquireWorker(arena, scheduler, Controller, 0));
        arena[1] = 2;
        arena[scheduler + 1] = 1;
        Assert.AreEqual(WarpPortableSchedulerLayout.Invalid, WarpPortableSchedulerServices.TryAcquireWorker(arena, scheduler, Controller, 0));
        WarpPortableHeapSchema heap = new([new(1, "ownership-admission:Object", WarpPortableHeapLayout.Class, 1, [1])]);
        uint[] template = heap.CreateArena(901, 32, 8, 8, 1, 32);
        Success(WarpPortableHeapServices.AcquireRoot(template, 0, 0, 0, WarpPortableHeapLayout.StrongRoot, 0, 0, 0));
        uint root = template[WarpPortableHeapLayout.Result];
        uint generation = template[WarpPortableHeapLayout.Result + 1];
        Success(WarpPortableHeapServices.ReleaseRoot(template, root, generation));
        uint[] original = (uint[])template.Clone();
        Assert.Throws<ArgumentException>(() => Schema(1, 1, 1, outputs: [0]).AttachToEmptyHeap(template));
        CollectionAssert.AreEqual(original, template);
    }

    private static uint RootEntry(uint[] arena, uint root) =>
        arena[WarpPortableHeapLayout.RootStart] + (root - 1) * WarpPortableHeapLayout.RootWords;

    private static uint[] ReservedRows(uint[] arena, uint scheduler) =>
        arena.AsSpan((int)arena[WarpPortableHeapLayout.RootStart], (int)(arena[scheduler + WarpPortableSchedulerLayout.WorkerCount] *
            arena[scheduler + WarpPortableSchedulerLayout.RootSlotsPerWorker] * WarpPortableHeapLayout.RootWords)).ToArray();
}
