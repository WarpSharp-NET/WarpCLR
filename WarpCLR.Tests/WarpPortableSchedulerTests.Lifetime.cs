using WarpCLR.Compiler;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableSchedulerTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void EscapedFaultsSelectLowestLogicalWorkerAndPreserveObjectIdentity(bool reverseArrival)
    {
        const uint workers = 67;
        uint[] arena = HeapArena(workers, 4, 1);
        uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
        Claim(arena, scheduler);
        uint[] generations = new uint[4];
        for (uint physical = 0; physical < 4; physical++)
        {
            (uint worker, uint generation) = Acquire(arena, scheduler, physical);
            Assert.AreEqual(physical, worker);
            generations[worker] = generation;
            Success(WarpPortableSchedulerServices.EnterUserFrame(arena, scheduler, Controller, worker, generation));
        }
        Success(WarpPortableHeapServices.AllocateObject(arena, 1));
        WarpPortableHeapReference exception = HeapResult(arena);
        uint[] escaped = reverseArrival ? [3, 1] : [1, 3];
        uint expectedWinner = escaped.Min();
        foreach (ref readonly uint worker in escaped.AsSpan())
        {
            Assert.AreEqual(WarpPortableSchedulerLayout.DispatchQuarantined,
                WarpPortableSchedulerServices.RecordEscapedFault(arena, scheduler, Controller, worker, generations[worker],
                    1, 100 + worker, 31 + worker, 90 + worker, 2, exception.Context, exception.Slot, exception.Generation));
            uint entry = Entry(arena, scheduler, worker);
            Assert.AreEqual(WarpPortableSchedulerLayout.ManagedFault, arena[entry + WarpPortableSchedulerLayout.FaultKind]);
            Assert.AreEqual(2u, arena[entry + WarpPortableSchedulerLayout.FaultDepth]);
            Assert.AreEqual(exception.Context, arena[entry + WarpPortableSchedulerLayout.FaultObject]);
            Assert.AreEqual(exception.Slot, arena[entry + WarpPortableSchedulerLayout.FaultObject + 1]);
            Assert.AreEqual(exception.Generation, arena[entry + WarpPortableSchedulerLayout.FaultObject + 2]);
            uint root = arena[entry + WarpPortableSchedulerLayout.FrameRootStart] + arena[entry + WarpPortableSchedulerLayout.FrameRootCapacity];
            uint reference = arena[WarpPortableHeapLayout.RootStart] + (root - 1) * WarpPortableHeapLayout.RootWords + WarpPortableHeapLayout.RootReference;
            Assert.AreEqual(exception.Slot, arena[reference + 1]);
        }
        Assert.AreEqual(expectedWinner, arena[scheduler + WarpPortableSchedulerLayout.FaultWinner]);
        Assert.AreEqual(2u, arena[scheduler + WarpPortableSchedulerLayout.FaultCount]);
        Assert.AreEqual(1u, arena[scheduler + WarpPortableSchedulerLayout.OutputQuarantined]);
        Assert.AreEqual(WarpPortableSchedulerLayout.DispatchQuarantined, WarpPortableSchedulerServices.ChargeUserSteps(arena, scheduler, Controller, 0, generations[0], 1));
        Assert.AreEqual(WarpPortableSchedulerLayout.DispatchQuarantined, WarpPortableSchedulerServices.YieldWorker(arena, scheduler, Controller, 0, generations[0]));
        Assert.AreEqual(WarpPortableSchedulerLayout.DispatchQuarantined, WarpPortableSchedulerServices.YieldWorker(arena, scheduler, Controller, 2, generations[2]));
        Assert.AreEqual(workers, arena[scheduler + WarpPortableSchedulerLayout.TerminalCount]);
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.ActiveWorkers]);
    }

    [TestMethod]
    public void EscapedFaultTerminatesBarrierWaitersWithoutLeavingAResidentDependency()
    {
        const uint workers = 73;
        uint[] arena = HeapArena(workers, 2, 1, [Grid(workers, 17)]);
        uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
        Claim(arena, scheduler);
        (uint waiting, uint waitingRun) = Acquire(arena, scheduler, 0);
        Success(WarpPortableSchedulerServices.ArriveBarrier(arena, scheduler, Controller, waiting, waitingRun, 0, 1, 17));
        (uint escaped, uint escapedRun) = Acquire(arena, scheduler, 1);
        Assert.AreEqual(WarpPortableSchedulerLayout.DispatchQuarantined,
            WarpPortableSchedulerServices.RecordEscapedFault(arena, scheduler, Controller, escaped, escapedRun, 1, 11, 5, 9, 1, 0, 0, 0));
        Assert.AreEqual(WarpPortableSchedulerLayout.Cancelled, arena[Entry(arena, scheduler, waiting) + WarpPortableSchedulerLayout.WorkerState]);
        Assert.AreEqual(WarpPortableSchedulerLayout.BarrierAborted, arena[Collective(arena, scheduler, 0) + WarpPortableSchedulerLayout.BarrierState]);
        Assert.AreEqual(workers, arena[scheduler + WarpPortableSchedulerLayout.TerminalCount]);
    }

    [TestMethod]
    public void CancellationKeepsPendingHelperRunnableThenQuarantinesAndDisposes()
    {
        const uint workers = 73;
        uint[] arena = HeapArena(workers, 2, 1);
        uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
        Claim(arena, scheduler);
        (uint owner, uint ownerRun) = Acquire(arena, scheduler, 0);
        Success(WarpPortableSchedulerServices.AcquireHeapService(arena, scheduler, Controller, owner, ownerRun));
        Success(WarpPortableHeapServices.AllocateObject(arena, 1));
        WarpPortableHeapReference pending = HeapResult(arena);
        Success(WarpPortableSchedulerServices.YieldWorker(arena, scheduler, Controller, owner, ownerRun));
        (uint peer, uint peerRun) = Acquire(arena, scheduler, 1);
        Success(WarpPortableSchedulerServices.RequestCancellation(arena, scheduler, Controller));
        Assert.AreEqual(pending, HeapResult(arena));
        Assert.AreEqual(WarpPortableSchedulerLayout.DispatchCancelled, WarpPortableSchedulerServices.ChargeUserSteps(arena, scheduler, Controller, peer, peerRun, 1));
        Assert.AreEqual(WarpPortableSchedulerLayout.DispatchCancelled, WarpPortableSchedulerServices.YieldWorker(arena, scheduler, Controller, peer, peerRun));
        (uint resumed, uint run) = Acquire(arena, scheduler, 0);
        Assert.AreEqual(owner, resumed);
        Assert.AreEqual(WarpPortableSchedulerLayout.RuntimeContinuation, arena[scheduler + WarpPortableSchedulerLayout.Result + 2]);
        Assert.AreEqual(WarpPortableSchedulerLayout.Yield, WarpPortableSchedulerServices.CancelWorker(arena, scheduler, Controller, owner, run));
        Assert.AreEqual(WarpPortableSchedulerLayout.Invalid, WarpPortableSchedulerServices.AbortHeapService(arena, scheduler, Controller, peer, peerRun));
        Success(WarpPortableSchedulerServices.AbortHeapService(arena, scheduler, Controller, owner, run));
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.LeaseState]);
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.PendingResult]);
        Assert.AreEqual(default, HeapResult(arena));
        Success(WarpPortableSchedulerServices.CancelWorker(arena, scheduler, Controller, owner, run));
        Assert.AreEqual(WarpPortableSchedulerLayout.CancelledContext, arena[scheduler + WarpPortableSchedulerLayout.ContextState]);
        Assert.AreEqual(workers, arena[scheduler + WarpPortableSchedulerLayout.TerminalCount]);
        Assert.AreEqual(1u, arena[scheduler + WarpPortableSchedulerLayout.OutputQuarantined]);
        Success(WarpPortableSchedulerServices.RequestDisposal(arena, scheduler, Controller));
        Success(WarpPortableSchedulerServices.FinishDisposal(arena, scheduler, Controller));
        Assert.AreEqual(0u, arena[WarpPortableHeapLayout.ActiveWorkers]);
        Assert.AreEqual(WarpPortableSchedulerLayout.ContextDisposed, WarpPortableSchedulerServices.TryAcquireWorker(arena, scheduler, Controller, 0));
    }

    [TestMethod]
    public void DisposalWaitsForResidentAndServiceOwnershipInsteadOfReusingTheirState()
    {
        uint[] arena = HeapArena(31, 2, 1);
        uint scheduler = arena[WarpPortableSchedulerLayout.HeapDescriptor];
        Claim(arena, scheduler);
        (uint owner, uint generation) = Acquire(arena, scheduler, 0);
        Success(WarpPortableSchedulerServices.AcquireHeapService(arena, scheduler, Controller, owner, generation));
        Success(WarpPortableSchedulerServices.RequestDisposal(arena, scheduler, Controller));
        Assert.AreEqual(WarpPortableSchedulerLayout.Yield, WarpPortableSchedulerServices.FinishDisposal(arena, scheduler, Controller));
        Success(WarpPortableSchedulerServices.AbortHeapService(arena, scheduler, Controller, owner, generation));
        Assert.AreEqual(WarpPortableSchedulerLayout.Yield, WarpPortableSchedulerServices.FinishDisposal(arena, scheduler, Controller));
        Success(WarpPortableSchedulerServices.CancelWorker(arena, scheduler, Controller, owner, generation));
        Success(WarpPortableSchedulerServices.FinishDisposal(arena, scheduler, Controller));
        Assert.AreEqual(WarpPortableSchedulerLayout.DisposedContext, arena[scheduler + WarpPortableSchedulerLayout.ContextState]);
    }

    [TestMethod]
    public void LateCancellationDoesNotReclassifyACompletedDispatch()
    {
        uint[] arena = Schema(1, 1, 1).CreateArena(211);
        Claim(arena, 0);
        (uint worker, uint generation) = Acquire(arena, 0, 0);
        Success(WarpPortableSchedulerServices.CompleteWorker(arena, 0, Controller, worker, generation));
        Success(WarpPortableSchedulerServices.RequestCancellation(arena, 0, Controller));
        Assert.AreEqual(WarpPortableSchedulerLayout.CompletedContext, arena[WarpPortableSchedulerLayout.ContextState]);
        Assert.AreEqual(0u, arena[WarpPortableSchedulerLayout.OutputQuarantined]);
    }

    [TestMethod]
    public void GenerationExhaustionNeverReusesBarrierRunOrGCEpochTokens()
    {
        uint[] arena = Schema(1, 1, 1).CreateArena(212);
        Claim(arena, 0);
        uint entry = Entry(arena, 0, 0);
        arena[entry + WarpPortableSchedulerLayout.RunGeneration] = uint.MaxValue;
        Assert.AreEqual(WarpPortableSchedulerLayout.DispatchQuarantined, WarpPortableSchedulerServices.TryAcquireWorker(arena, 0, Controller, 0));
        Assert.AreEqual(uint.MaxValue, arena[entry + WarpPortableSchedulerLayout.RunGeneration]);
        arena = Schema(1, 1, 1, barriers: [Grid(1, 17)]).CreateArena(213);
        Claim(arena, 0);
        (uint worker, uint generation) = Acquire(arena, 0, 0);
        arena[Collective(arena, 0, 0) + WarpPortableSchedulerLayout.BarrierGeneration] = uint.MaxValue;
        Assert.AreEqual(WarpPortableSchedulerLayout.DispatchQuarantined, WarpPortableSchedulerServices.ArriveBarrier(arena, 0, Controller, worker, generation, 0, uint.MaxValue, 17));
        Assert.AreEqual(uint.MaxValue, arena[Collective(arena, 0, 0) + WarpPortableSchedulerLayout.BarrierGeneration]);
        arena = Schema(1, 1, 1).CreateArena(214);
        Claim(arena, 0);
        arena[WarpPortableSchedulerLayout.GCEpoch] = uint.MaxValue;
        Assert.AreEqual(WarpPortableSchedulerLayout.DispatchQuarantined, WarpPortableSchedulerServices.RequestCollection(arena, 0, Controller));
        Assert.AreEqual(uint.MaxValue, arena[WarpPortableSchedulerLayout.GCEpoch]);
    }
}
