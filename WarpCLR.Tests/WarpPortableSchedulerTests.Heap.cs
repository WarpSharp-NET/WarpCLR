using WarpCLR.Compiler;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableSchedulerTests
{
    [TestMethod]
    public void PendingResultYieldRetainsEveryWorkerUntilPrecisePublicationAndGC()
    {
        const uint workers = 37;
        uint[] arena = HeapArena(workers, 2, 1);
        uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
        Claim(arena, scheduler);
        (uint owner, uint firstRun) = Acquire(arena, scheduler, 0);
        Success(WarpPortableSchedulerServices.AcquireHeapService(arena, scheduler, Controller, owner, firstRun));
        Success(WarpPortableHeapServices.AllocateObject(arena, 1));
        WarpPortableHeapReference value = HeapResult(arena);
        Success(WarpPortableSchedulerServices.YieldWorker(arena, scheduler, Controller, owner, firstRun));
        Success(WarpPortableSchedulerServices.RequestCollection(arena, scheduler, Controller));
        const uint epoch = 1;
        Assert.AreEqual(WarpPortableSchedulerLayout.Yield, WarpPortableSchedulerServices.ParkForCollection(arena, scheduler, Controller, owner, firstRun, epoch));
        for (uint worker = 1; worker < workers; worker++)
        {
            PublishEmpty(arena, scheduler, worker, epoch);
            Success(WarpPortableSchedulerServices.ParkForCollection(arena, scheduler, Controller, worker, 0, epoch));
        }
        Assert.AreEqual(value, HeapResult(arena), "Peer parking must preserve unresolved heap result scratch.");
        Assert.AreEqual(WarpPortableSchedulerLayout.NeedGCParking, WarpPortableSchedulerServices.BeginCollection(arena, scheduler, Controller, epoch));
        (uint resumed, uint run) = Acquire(arena, scheduler, 1);
        Assert.AreEqual(owner, resumed);
        Assert.AreEqual(WarpPortableSchedulerLayout.RuntimeContinuation, arena[scheduler + WarpPortableSchedulerLayout.Result + 2]);
        Assert.AreEqual(WarpPortableSchedulerLayout.Yield, WarpPortableSchedulerServices.ChargeUserSteps(arena, scheduler, Controller, owner, run, 1));
        Assert.AreEqual(WarpPortableSchedulerLayout.NeedRootPublication, WarpPortableSchedulerServices.AcknowledgeHeapResult(arena, scheduler, Controller, owner, run, 0));
        PublishEmpty(arena, scheduler, owner, epoch);
        Assert.AreEqual(WarpPortableSchedulerLayout.NeedRootPublication, WarpPortableSchedulerServices.AcknowledgeHeapResult(arena, scheduler, Controller, owner, run, 1));
        PutFrameReference(arena, scheduler, owner, value);
        Success(WarpPortableSchedulerServices.PublishRoots(arena, scheduler, Controller, owner, run, epoch, 1, 2, 4, 22));
        Success(WarpPortableSchedulerServices.AcknowledgeHeapResult(arena, scheduler, Controller, owner, run, 2));
        Success(WarpPortableSchedulerServices.ReleaseHeapService(arena, scheduler, Controller, owner, run));
        Success(WarpPortableSchedulerServices.ParkForCollection(arena, scheduler, Controller, owner, run, epoch));
        Collect(arena, scheduler, epoch);
        Assert.AreEqual(1u, arena[WarpPortableHeapLayout.LiveObjects]);
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.ParkedWorkers]);
        Assert.AreEqual(workers, arena[WarpPortableHeapLayout.ActiveWorkers]);
        Assert.AreEqual(0u, arena[Entry(arena, scheduler, owner) + WarpPortableSchedulerLayout.StepSpentLow]);
        DropReferenceAndConfirmPreciseCollection(arena, scheduler, workers, owner, run, value);
    }

    private static void DropReferenceAndConfirmPreciseCollection(uint[] arena, uint scheduler, uint workers,
        uint owner, uint run, WarpPortableHeapReference value)
    {
        Success(WarpPortableSchedulerServices.RequestCollection(arena, scheduler, Controller));
        for (uint worker = 0; worker < workers; worker++)
        {
            uint entry = Entry(arena, scheduler, worker);
            uint state = arena[entry + WarpPortableSchedulerLayout.LogicalStackBase];
            arena[state + 8] = value.Context;
            arena[state + 9] = value.Slot;
            arena[state + 10] = value.Generation;
            PublishEmpty(arena, scheduler, worker, 2);
            Success(WarpPortableSchedulerServices.ParkForCollection(arena, scheduler, Controller, worker,
                arena[entry + WarpPortableSchedulerLayout.RunGeneration], 2));
        }
        Collect(arena, scheduler, 2);
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.LiveObjects], "Unmapped source/frame scalar bits must not retain the object.");
        uint revision = arena[Entry(arena, scheduler, owner) + WarpPortableSchedulerLayout.RootRevision];
        Assert.AreEqual(WarpPortableSchedulerLayout.Invalid, WarpPortableSchedulerServices.PublishRoots(arena, scheduler, Controller,
            owner, run, 2, 1, revision + 1, 4, 22), "A stale tuple in the frame is rejected when selected by a precise map.");
        Assert.AreEqual(revision, arena[Entry(arena, scheduler, owner) + WarpPortableSchedulerLayout.RootRevision]);
    }

    [TestMethod]
    public void UnpublishedForeignOrMismatchedRootMapsCannotReportGCQuiescence()
    {
        uint[] arena = HeapArena(19, 1, 1);
        uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
        Claim(arena, scheduler);
        Success(WarpPortableSchedulerServices.RequestCollection(arena, scheduler, Controller));
        Assert.AreEqual(WarpPortableSchedulerLayout.NeedRootPublication, WarpPortableSchedulerServices.ParkForCollection(arena, scheduler, Controller, 0, 0, 1));
        Assert.AreEqual(WarpPortableSchedulerLayout.Invalid, WarpPortableSchedulerServices.PublishRoots(arena, scheduler, Controller, 0, 0, 0, 0, 1, 0, 0));
        Assert.AreEqual(WarpPortableSchedulerLayout.Invalid, WarpPortableSchedulerServices.PublishRoots(arena, scheduler, Controller, 0, 0, 1, 1, 1, 0, 0));
        PutFrameReference(arena, scheduler, 0, new WarpPortableHeapReference(999, 1, 1));
        Assert.AreEqual(WarpPortableSchedulerLayout.Invalid, WarpPortableSchedulerServices.PublishRoots(arena, scheduler, Controller, 0, 0, 1, 1, 1, 4, 22));
        Assert.AreEqual(0u, arena[scheduler + WarpPortableSchedulerLayout.GCParkedCount]);
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.ParkedWorkers]);
        PublishEmpty(arena, scheduler, 0, 1);
        Success(WarpPortableSchedulerServices.ParkForCollection(arena, scheduler, Controller, 0, 0, 1));
        Assert.AreEqual(WarpPortableSchedulerLayout.NeedGCParking, WarpPortableSchedulerServices.BeginCollection(arena, scheduler, Controller, 1));
    }

    [TestMethod]
    public void BarrierReleaseWhilePeersAreGCParkedUpdatesTheirResumeStates()
    {
        const uint workers = 17;
        uint[] arena = HeapArena(workers, 2, 1, [Grid(workers, 17)]);
        uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
        Claim(arena, scheduler);
        for (uint arrived = 0; arrived < workers - 1; arrived++)
        {
            (uint worker, uint generation) = Acquire(arena, scheduler, arrived & 1);
            Success(WarpPortableSchedulerServices.ArriveBarrier(arena, scheduler, Controller, worker, generation, 0, 1, 17));
        }
        (uint last, uint lastRun) = Acquire(arena, scheduler, 0);
        Success(WarpPortableSchedulerServices.ChargeUserSteps(arena, scheduler, Controller, last, lastRun, 1));
        Success(WarpPortableSchedulerServices.RequestCollection(arena, scheduler, Controller));
        for (uint worker = 0; worker < workers - 1; worker++)
        {
            uint entry = Entry(arena, scheduler, worker);
            PublishEmpty(arena, scheduler, worker, 1);
            Success(WarpPortableSchedulerServices.ParkForCollection(arena, scheduler, Controller, worker,
                arena[entry + WarpPortableSchedulerLayout.RunGeneration], 1));
            Assert.AreEqual(WarpPortableSchedulerLayout.ParkedBarrier, arena[entry + WarpPortableSchedulerLayout.ResumeState]);
        }
        Success(WarpPortableSchedulerServices.ArriveBarrier(arena, scheduler, Controller, last, lastRun, 0, 1, 17));
        Assert.AreEqual(2u, arena[Collective(arena, scheduler, 0) + WarpPortableSchedulerLayout.BarrierGeneration]);
        for (uint worker = 0; worker < workers - 1; worker++)
        {
            Assert.AreEqual(WarpPortableSchedulerLayout.Ready, arena[Entry(arena, scheduler, worker) + WarpPortableSchedulerLayout.ResumeState]);
        }
        PublishEmpty(arena, scheduler, last, 1);
        Success(WarpPortableSchedulerServices.ParkForCollection(arena, scheduler, Controller, last, lastRun, 1));
        Collect(arena, scheduler, 1);
        for (uint worker = 0; worker < workers; worker++)
        {
            Assert.AreEqual(WarpPortableSchedulerLayout.Ready, arena[Entry(arena, scheduler, worker) + WarpPortableSchedulerLayout.WorkerState]);
        }
    }

    [TestMethod]
    public void GCResumesAnIncompleteBarrierWithoutFabricatingArrivals()
    {
        const uint workers = 23;
        uint[] arena = HeapArena(workers, 1, 1, [Grid(workers, 17)]);
        uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
        Claim(arena, scheduler);
        (uint first, uint generation) = Acquire(arena, scheduler, 0);
        Success(WarpPortableSchedulerServices.ArriveBarrier(arena, scheduler, Controller, first, generation, 0, 1, 17));
        Success(WarpPortableSchedulerServices.RequestCollection(arena, scheduler, Controller));
        for (uint worker = 0; worker < workers; worker++)
        {
            PublishEmpty(arena, scheduler, worker, 1);
            uint run = arena[Entry(arena, scheduler, worker) + WarpPortableSchedulerLayout.RunGeneration];
            Success(WarpPortableSchedulerServices.ParkForCollection(arena, scheduler, Controller, worker, run, 1));
        }
        Collect(arena, scheduler, 1);
        Assert.AreEqual(WarpPortableSchedulerLayout.ParkedBarrier, arena[Entry(arena, scheduler, first) + WarpPortableSchedulerLayout.WorkerState]);
        Assert.AreEqual(1u, arena[Collective(arena, scheduler, 0) + WarpPortableSchedulerLayout.BarrierArrived]);
        for (uint arrived = 1; arrived < workers; arrived++)
        {
            (uint worker, uint run) = Acquire(arena, scheduler, 0);
            Success(WarpPortableSchedulerServices.ArriveBarrier(arena, scheduler, Controller, worker, run, 0, 1, 17));
        }
        Assert.AreEqual(2u, arena[Collective(arena, scheduler, 0) + WarpPortableSchedulerLayout.BarrierGeneration]);
    }

    [TestMethod]
    public void ServiceWaiterParkedForGCWakesAfterOwnerPublicationAndRelease()
    {
        const uint workers = 19;
        uint[] arena = HeapArena(workers, 2, 1);
        uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
        Claim(arena, scheduler);
        (uint owner, uint ownerRun) = Acquire(arena, scheduler, 0);
        Success(WarpPortableSchedulerServices.AcquireHeapService(arena, scheduler, Controller, owner, ownerRun));
        Success(WarpPortableHeapServices.AllocateObject(arena, 1));
        WarpPortableHeapReference value = HeapResult(arena);
        (uint waiter, uint waiterRun) = Acquire(arena, scheduler, 1);
        Assert.AreEqual(WarpPortableSchedulerLayout.Yield, WarpPortableSchedulerServices.AcquireHeapService(arena, scheduler, Controller, waiter, waiterRun));
        Assert.AreEqual(WarpPortableSchedulerLayout.WaitingService, arena[Entry(arena, scheduler, waiter) + WarpPortableSchedulerLayout.WorkerState]);
        Success(WarpPortableSchedulerServices.YieldWorker(arena, scheduler, Controller, owner, ownerRun));
        Success(WarpPortableSchedulerServices.RequestCollection(arena, scheduler, Controller));
        for (uint worker = 1; worker < workers; worker++)
        {
            PublishEmpty(arena, scheduler, worker, 1);
            uint run = arena[Entry(arena, scheduler, worker) + WarpPortableSchedulerLayout.RunGeneration];
            Success(WarpPortableSchedulerServices.ParkForCollection(arena, scheduler, Controller, worker, run, 1));
        }
        Assert.AreEqual(WarpPortableSchedulerLayout.WaitingService, arena[Entry(arena, scheduler, waiter) + WarpPortableSchedulerLayout.ResumeState]);
        (owner, ownerRun) = Acquire(arena, scheduler, 0);
        Assert.AreEqual(value, HeapResult(arena));
        PutFrameReference(arena, scheduler, owner, value);
        Success(WarpPortableSchedulerServices.PublishRoots(arena, scheduler, Controller, owner, ownerRun, 1, 1, 1, 4, 22));
        Success(WarpPortableSchedulerServices.AcknowledgeHeapResult(arena, scheduler, Controller, owner, ownerRun, 1));
        Success(WarpPortableSchedulerServices.ReleaseHeapService(arena, scheduler, Controller, owner, ownerRun));
        Assert.AreEqual(WarpPortableSchedulerLayout.Ready, arena[Entry(arena, scheduler, waiter) + WarpPortableSchedulerLayout.ResumeState]);
        Success(WarpPortableSchedulerServices.ParkForCollection(arena, scheduler, Controller, owner, ownerRun, 1));
        Collect(arena, scheduler, 1);
        (uint resumed, uint resumedRun) = Acquire(arena, scheduler, 1);
        Assert.AreEqual(waiter, resumed);
        Success(WarpPortableSchedulerServices.AcquireHeapService(arena, scheduler, Controller, resumed, resumedRun));
        Success(WarpPortableSchedulerServices.ReleaseHeapService(arena, scheduler, Controller, resumed, resumedRun));
    }

    [TestMethod]
    public void ScalarServiceResultAlsoRequiresAcknowledgementBeforeLeaseReuse()
    {
        uint[] arena = HeapArena(3, 1, 1);
        uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
        Claim(arena, scheduler);
        (uint worker, uint run) = Acquire(arena, scheduler, 0);
        Success(WarpPortableSchedulerServices.AcquireHeapService(arena, scheduler, Controller, worker, run));
        arena[WarpPortableHeapLayout.Result] = 0xFEDCBA98;
        Success(WarpPortableSchedulerServices.CaptureServiceResult(arena, scheduler, Controller, worker, run, 0));
        Assert.AreEqual(WarpPortableSchedulerLayout.NeedRootPublication, WarpPortableSchedulerServices.ReleaseHeapService(arena, scheduler, Controller, worker, run));
        Success(WarpPortableSchedulerServices.RequestCollection(arena, scheduler, Controller));
        Assert.AreEqual(0xFEDCBA98u, arena[WarpPortableHeapLayout.Result]);
        PublishEmpty(arena, scheduler, worker, 1);
        Success(WarpPortableSchedulerServices.AcknowledgeHeapResult(arena, scheduler, Controller, worker, run, 1));
        Success(WarpPortableSchedulerServices.ReleaseHeapService(arena, scheduler, Controller, worker, run));
    }

    private static uint[] HeapArena(uint workers, uint residents, uint quantum, IEnumerable<WarpPortableSchedulerBarrierLayout>? barriers = null, IEnumerable<uint>? outputs = null)
    {
        WarpPortableHeapSchema heap = new([new(1, "scheduler-test:Object", WarpPortableHeapLayout.Class, 6, [1],
            references: [new(0, 1), new(3, 1)])]);
        uint payload = workers * 32 + 256;
        uint[] arena = heap.CreateArena(201, payload, workers * 2 + 8, workers * 3 + 8, workers, payload);
        return Schema(workers, residents, quantum, barriers: barriers, outputs: outputs).AttachToEmptyHeap(arena);
    }

    private static WarpPortableHeapReference HeapResult(uint[] arena) =>
        new(arena[WarpPortableHeapLayout.Result], arena[WarpPortableHeapLayout.Result + 1], arena[WarpPortableHeapLayout.Result + 2]);

    private static void PutFrameReference(uint[] arena, uint scheduler, uint worker, WarpPortableHeapReference value)
    {
        uint state = arena[Entry(arena, scheduler, worker) + WarpPortableSchedulerLayout.LogicalStackBase];
        arena[state] = value.Context;
        arena[state + 1] = value.Slot;
        arena[state + 2] = value.Generation;
    }

    private static void PublishEmpty(uint[] arena, uint scheduler, uint worker, uint epoch)
    {
        uint entry = Entry(arena, scheduler, worker);
        Success(WarpPortableSchedulerServices.PublishRoots(arena, scheduler, Controller, worker,
            arena[entry + WarpPortableSchedulerLayout.RunGeneration], epoch, 0,
            arena[entry + WarpPortableSchedulerLayout.RootRevision] + 1, 0, 0));
    }

    private static void Collect(uint[] arena, uint scheduler, uint epoch)
    {
        Success(WarpPortableSchedulerServices.BeginCollection(arena, scheduler, Controller, epoch));
        Assert.AreEqual(WarpPortableSchedulerLayout.Yield, WarpPortableSchedulerServices.RequestCancellation(arena, scheduler, Controller));
        Success(WarpPortableHeapServices.Collect(arena));
        Success(WarpPortableSchedulerServices.FinishCollection(arena, scheduler, Controller, epoch, 0));
    }
}
