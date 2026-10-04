using WarpCLR.Compiler;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableSchedulerTests
{
    [TestMethod]
    public void CompletedRedispatchPreservesHeapHostAndOutputRootsWhileResettingUserState()
    {
        WarpPortableHeapSchema heap = new([new(1, "scheduler-redispatch:Object", WarpPortableHeapLayout.Class, 2, [1], staticWords: 1)]);
        uint[] arena = Schema(1, 1, 1, barriers: [Grid(1, 17)], outputs: [0]).AttachToEmptyHeap(heap.CreateArena(601, 64, 8, 8, 1, 64));
        uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
        Claim(arena, scheduler);
        Success(WarpPortableHeapServices.BeginTypeInitialization(arena, 1, 0));
        Success(WarpPortableHeapServices.CompleteTypeInitialization(arena, 1, 0));
        Success(WarpPortableHeapServices.WriteStaticWord(arena, 1, 0, 0xFEDCBA98));
        Success(WarpPortableHeapServices.AllocateObject(arena, 1));
        WarpPortableHeapReference value = HeapResult(arena);
        Success(WarpPortableHeapServices.WriteWord(arena, value.Context, value.Slot, value.Generation, 0, 0x12345678));
        Success(WarpPortableHeapServices.AcquireRoot(arena, value.Context, value.Slot, value.Generation, WarpPortableHeapLayout.StrongRoot, 0, 0, 0));
        uint hostRoot = arena[WarpPortableHeapLayout.Result];
        uint hostGeneration = arena[WarpPortableHeapLayout.Result + 1];
        CompleteReferenceDispatch(arena, scheduler, value);
        Success(WarpPortableSchedulerServices.RequestCollection(arena, scheduler, Controller));
        Collect(arena, scheduler, 1);
        Assert.AreEqual(1u, arena[WarpPortableHeapLayout.LiveObjects]);
        Success(WarpPortableSchedulerServices.BeginDispatch(arena, scheduler, Controller));
        AssertRedispatchReset(arena, scheduler);
        Success(WarpPortableHeapServices.ReadRoot(arena, hostRoot, hostGeneration));
        Assert.AreEqual(value, HeapResult(arena));
        Success(WarpPortableHeapServices.ReadWord(arena, value.Context, value.Slot, value.Generation, 0));
        Assert.AreEqual(0x12345678u, arena[WarpPortableHeapLayout.Result]);
        Success(WarpPortableHeapServices.ReadStaticWord(arena, 1, 0));
        Assert.AreEqual(0xFEDCBA98u, arena[WarpPortableHeapLayout.Result]);
        Success(WarpPortableHeapServices.BeginTypeInitialization(arena, 1, 0));
        Assert.AreEqual(3u, arena[WarpPortableHeapLayout.Result]);
        Success(WarpPortableHeapServices.ReleaseRoot(arena, hostRoot, hostGeneration));
        ResumeAndReplacePreviousOutput(arena, scheduler, value);
        Success(WarpPortableSchedulerServices.RequestCollection(arena, scheduler, Controller));
        Collect(arena, scheduler, 3);
        Assert.AreEqual(1u, arena[WarpPortableHeapLayout.LiveObjects]);
        Assert.AreEqual(WarpPortableSchedulerLayout.Invalid, WarpPortableSchedulerServices.ReleaseOutputRoots(arena, scheduler, Controller, 0, 1));
        Success(WarpPortableSchedulerServices.ReleaseOutputRoots(arena, scheduler, Controller, 0, 2));
        Success(WarpPortableSchedulerServices.RequestCollection(arena, scheduler, Controller));
        Collect(arena, scheduler, 4);
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.LiveObjects]);
        Success(WarpPortableSchedulerServices.BeginDispatch(arena, scheduler, Controller));
        Assert.AreEqual(3u, arena[scheduler + WarpPortableSchedulerLayout.DispatchGeneration]);
        Assert.AreEqual(0u, arena[scheduler + WarpPortableSchedulerLayout.OutputPendingWorkers]);
    }

    [TestMethod]
    public void OutputRootPublicationRejectsMalformedHandlesAndDisposalReleasesUnacknowledgedRoots()
    {
        uint[] arena = HeapArena(17, 1, 1, outputs: [0]);
        uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
        Claim(arena, scheduler);
        Success(WarpPortableHeapServices.AllocateObject(arena, 1));
        WarpPortableHeapReference value = HeapResult(arena);
        (uint worker, uint generation) = Acquire(arena, scheduler, 0);
        PutFrameReference(arena, scheduler, worker, new(value.Context + 1, value.Slot, value.Generation));
        Assert.AreEqual(WarpPortableSchedulerLayout.Invalid, WarpPortableSchedulerServices.PublishOutputRoots(arena, scheduler, Controller, worker, generation, 1));
        Assert.AreEqual(0u, arena[scheduler + WarpPortableSchedulerLayout.OutputPendingWorkers]);
        PutFrameReference(arena, scheduler, worker, value);
        Assert.AreEqual(WarpPortableSchedulerLayout.Invalid, WarpPortableSchedulerServices.PublishOutputRoots(arena, scheduler, Controller, worker, generation, 2));
        Success(WarpPortableSchedulerServices.PublishOutputRoots(arena, scheduler, Controller, worker, generation, 1));
        Assert.AreEqual(WarpPortableSchedulerLayout.Invalid, WarpPortableSchedulerServices.ReleaseOutputRoots(arena, scheduler, Controller, worker, 1));
        Success(WarpPortableSchedulerServices.RequestDisposal(arena, scheduler, Controller));
        Success(WarpPortableSchedulerServices.CancelWorker(arena, scheduler, Controller, worker, generation));
        Success(WarpPortableSchedulerServices.FinishDisposal(arena, scheduler, Controller));
        Assert.AreEqual(0u, arena[scheduler + WarpPortableSchedulerLayout.OutputPendingWorkers]);
        Success(WarpPortableHeapServices.RequestCollection(arena));
        Success(WarpPortableHeapServices.Collect(arena));
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.LiveObjects]);
    }

    [TestMethod]
    public void DispatchAndRootPublicationIdentitiesExhaustWithoutWrapping()
    {
        uint[] arena = Schema(1, 1, 1).CreateArena(602);
        Claim(arena, 0);
        (uint worker, uint generation) = Acquire(arena, 0, 0);
        uint entry = Entry(arena, 0, worker);
        arena[entry + WarpPortableSchedulerLayout.RootRevision] = uint.MaxValue;
        Assert.AreEqual(WarpPortableSchedulerLayout.DispatchQuarantined, WarpPortableSchedulerServices.PublishRoots(arena, 0, Controller, worker, generation, 0, 0, 0, 0, 0));
        Assert.AreEqual(uint.MaxValue, arena[entry + WarpPortableSchedulerLayout.RootRevision]);
        Assert.AreEqual(WarpPortableSchedulerLayout.GenerationExhausted, arena[entry + WarpPortableSchedulerLayout.FaultKind]);
        arena = Schema(1, 1, 1).CreateArena(603);
        Claim(arena, 0);
        (worker, generation) = Acquire(arena, 0, 0);
        Success(WarpPortableSchedulerServices.CompleteWorker(arena, 0, Controller, worker, generation));
        arena[WarpPortableSchedulerLayout.DispatchGeneration] = uint.MaxValue;
        Assert.AreEqual(WarpPortableSchedulerLayout.DispatchQuarantined, WarpPortableSchedulerServices.BeginDispatch(arena, 0, Controller));
        Assert.AreEqual(uint.MaxValue, arena[WarpPortableSchedulerLayout.DispatchGeneration]);
    }

    [TestMethod]
    [DataRow(1u)]
    [DataRow(2u)]
    [DataRow(3u)]
    [DataRow(4u)]
    public void RedispatchRequiresCompletedContext(uint state)
    {
        uint[] arena = Schema(3, 1, 1).CreateArena(604);
        Claim(arena, 0);
        if (state == 2)
        {
            (uint worker, uint generation) = Acquire(arena, 0, 0);
            Assert.AreEqual(WarpPortableSchedulerLayout.DispatchQuarantined,
                WarpPortableSchedulerServices.RecordEscapedFault(arena, 0, Controller, worker, generation, 1, 1, 1, 1, 1, 0, 0, 0));
        }
        else if (state == 3)
        {
            Success(WarpPortableSchedulerServices.RequestCancellation(arena, 0, Controller));
        }
        else if (state == 4)
        {
            Success(WarpPortableSchedulerServices.RequestDisposal(arena, 0, Controller));
            Success(WarpPortableSchedulerServices.FinishDisposal(arena, 0, Controller));
        }
        uint expected = state == 4 ? WarpPortableSchedulerLayout.ContextDisposed : WarpPortableSchedulerLayout.Invalid;
        Assert.AreEqual(expected, WarpPortableSchedulerServices.BeginDispatch(arena, 0, Controller));
        Assert.AreEqual(1u, arena[WarpPortableSchedulerLayout.DispatchGeneration]);
    }

    [TestMethod]
    public void CorruptedDescriptorBanksFailBeforeAnyIndexedMemoryAccess()
    {
        foreach (uint offset in new[] { WarpPortableSchedulerLayout.OutputOffsets, WarpPortableSchedulerLayout.LogicalStateStart,
            WarpPortableSchedulerLayout.RootMapStart, WarpPortableSchedulerLayout.WorkerStart })
        {
            uint[] arena = Schema(1, 1, 1, outputs: [0]).CreateArena(605);
            Claim(arena, 0);
            arena[offset] = uint.MaxValue;
            Assert.AreEqual(WarpPortableSchedulerLayout.Invalid, WarpPortableSchedulerServices.TryAcquireWorker(arena, 0, Controller, 0));
        }
        uint[] linked = HeapArena(1, 1, 1, outputs: [0]);
        uint scheduler = linked[WarpPortableSchedulerLayout.HeapDescriptor];
        Claim(linked, scheduler);
        linked[WarpPortableHeapLayout.RootCount] = uint.MaxValue;
        Assert.AreEqual(WarpPortableSchedulerLayout.Invalid, WarpPortableSchedulerServices.TryAcquireWorker(linked, scheduler, Controller, 0));
        linked = HeapArena(1, 1, 1, outputs: [0]);
        scheduler = linked[WarpPortableSchedulerLayout.HeapDescriptor];
        Claim(linked, scheduler);
        linked[Entry(linked, scheduler, 0) + WarpPortableSchedulerLayout.LogicalStackBase] = uint.MaxValue;
        Assert.AreEqual(WarpPortableSchedulerLayout.Invalid, WarpPortableSchedulerServices.TryAcquireWorker(linked, scheduler, Controller, 0));
    }

    private static void CompleteReferenceDispatch(uint[] arena, uint scheduler, WarpPortableHeapReference value)
    {
        (uint worker, uint generation) = Acquire(arena, scheduler, 0);
        uint source = arena[Entry(arena, scheduler, worker) + WarpPortableSchedulerLayout.LogicalStackBase];
        arena[source + 8] = 0xA5A5A5A5;
        PutFrameReference(arena, scheduler, worker, value);
        Success(WarpPortableSchedulerServices.ChargeUserSteps(arena, scheduler, Controller, worker, generation, 17));
        Success(WarpPortableSchedulerServices.EnterUserFrame(arena, scheduler, Controller, worker, generation));
        Success(WarpPortableSchedulerServices.PublishRoots(arena, scheduler, Controller, worker, generation, 0, 1, 1, 4, 22));
        Assert.AreEqual(WarpPortableSchedulerLayout.NeedRootPublication, WarpPortableSchedulerServices.CompleteWorker(arena, scheduler, Controller, worker, generation));
        Success(WarpPortableSchedulerServices.PublishOutputRoots(arena, scheduler, Controller, worker, generation, 1));
        Assert.AreEqual(WarpPortableSchedulerLayout.NeedRootPublication, WarpPortableSchedulerServices.CompleteWorker(arena, scheduler, Controller, worker, generation));
        Success(WarpPortableSchedulerServices.AcknowledgeOutputRoots(arena, scheduler, Controller, worker, generation, 1));
        Assert.AreEqual(WarpPortableSchedulerLayout.Invalid, WarpPortableSchedulerServices.ReleaseOutputRoots(arena, scheduler, Controller, worker, 1));
        Success(WarpPortableSchedulerServices.CompleteWorker(arena, scheduler, Controller, worker, generation));
        Assert.AreEqual(WarpPortableSchedulerLayout.CompletedContext, arena[scheduler + WarpPortableSchedulerLayout.ContextState]);
    }

    private static void AssertRedispatchReset(uint[] arena, uint scheduler)
    {
        uint entry = Entry(arena, scheduler, 0);
        uint source = arena[entry + WarpPortableSchedulerLayout.LogicalStackBase];
        Assert.AreEqual(2u, arena[scheduler + WarpPortableSchedulerLayout.DispatchGeneration]);
        Assert.AreEqual(2u, arena[Collective(arena, scheduler, 0) + WarpPortableSchedulerLayout.BarrierGeneration]);
        Assert.AreEqual(1u, arena[entry + WarpPortableSchedulerLayout.RunGeneration]);
        Assert.AreEqual(1u, arena[entry + WarpPortableSchedulerLayout.RootRevision]);
        Assert.AreEqual(0u, arena[entry + WarpPortableSchedulerLayout.StepSpentLow]);
        Assert.AreEqual(4096u, arena[entry + WarpPortableSchedulerLayout.StepRemainingLow]);
        Assert.AreEqual(1u, arena[entry + WarpPortableSchedulerLayout.UserStackDepth]);
        Assert.AreEqual(WarpPortableSchedulerLayout.OutputInherited, arena[entry + WarpPortableSchedulerLayout.OutputState]);
        for (uint index = 0; index < 16; index++)
        {
            Assert.AreEqual(0u, arena[source + index]);
        }
        Assert.AreEqual(1u, arena[WarpPortableHeapLayout.ActiveWorkers]);
    }

    private static void ResumeAndReplacePreviousOutput(uint[] arena, uint scheduler, WarpPortableHeapReference value)
    {
        (uint worker, uint generation) = Acquire(arena, scheduler, 0);
        Success(WarpPortableSchedulerServices.RequestCollection(arena, scheduler, Controller));
        PublishEmpty(arena, scheduler, worker, 2);
        Success(WarpPortableSchedulerServices.ParkForCollection(arena, scheduler, Controller, worker, generation, 2));
        Collect(arena, scheduler, 2);
        Assert.AreEqual(1u, arena[WarpPortableHeapLayout.LiveObjects]);
        (worker, generation) = Acquire(arena, scheduler, 0);
        PutFrameReference(arena, scheduler, worker, value);
        Success(WarpPortableSchedulerServices.PublishRoots(arena, scheduler, Controller, worker, generation, 2, 1, 3, 4, 22));
        Assert.AreEqual(WarpPortableSchedulerLayout.NeedOutputRelease, WarpPortableSchedulerServices.PublishOutputRoots(arena, scheduler, Controller, worker, generation, 2));
        Assert.AreEqual(WarpPortableSchedulerLayout.NeedOutputRelease, WarpPortableSchedulerServices.TryAcquireWorker(arena, scheduler, Controller, 0));
        Assert.AreEqual(0u, arena[scheduler + WarpPortableSchedulerLayout.OutputQuarantined]);
        Success(WarpPortableSchedulerServices.ReleaseOutputRoots(arena, scheduler, Controller, worker, 1));
        (worker, generation) = Acquire(arena, scheduler, 0);
        Success(WarpPortableSchedulerServices.PublishOutputRoots(arena, scheduler, Controller, worker, generation, 2));
        Success(WarpPortableSchedulerServices.AcknowledgeOutputRoots(arena, scheduler, Controller, worker, generation, 2));
        Success(WarpPortableSchedulerServices.CompleteWorker(arena, scheduler, Controller, worker, generation));
    }
}
