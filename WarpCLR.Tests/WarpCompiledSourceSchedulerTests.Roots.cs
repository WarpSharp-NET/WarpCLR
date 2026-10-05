using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpCompiledSourceSchedulerTests
{
    [TestMethod]
    public void PendingGeneratedHeapResultPreventsCollectionUntilPublicationAndLeaseAcknowledgement()
    {
        WarpCompiledSourceContext context = Create(nameof(Kernels.Identity), 6, 1, [new uint[6], new uint[6], new uint[6]]);
        for (uint worker = 0; worker < 6; worker++) { context.QueueEntryAllocation(worker, ObjectType(context)); }
        WarpCompiledWorkerTicket? pending = null;
        for (int attempt = 0; attempt < 10000 && pending is null; attempt++)
        {
            WarpCompiledWorkerTicket? ticket = context.Claim(0);
            if (ticket is null) { continue; }
            context.Execute(ticket);
            if (context.Arena[WarpPortableHeapLayout.PendingResult] != 0) { pending = ticket; }
            else { context.Commit(ticket); }
        }
        Assert.IsNotNull(pending);
        Assert.AreEqual(1u, context.Arena[WarpPortableHeapLayout.LeaseState]);
        uint[] unpublished = context.Arena.Skip(checked((int)WarpPortableHeapLayout.Result)).Take(3).ToArray();
        Assert.AreEqual(0u, context.RequestCollection());
        Assert.AreEqual(WarpPortableSchedulerLayout.NeedGCParking, context.AdvanceCollection());
        CollectionAssert.AreEqual(unpublished, context.Arena.Skip(checked((int)WarpPortableHeapLayout.Result)).Take(3).ToArray());
        Assert.AreEqual(0u, context.Arena[context.Scheduler + WarpPortableSchedulerLayout.GCParkedCount]);
        context.Commit(pending);
        Assert.AreEqual(0u, context.Arena[WarpPortableHeapLayout.LeaseState]);
        Assert.AreEqual(0u, context.Arena[WarpPortableHeapLayout.PendingResult]);
        Assert.AreEqual(0u, context.AdvanceCollection());
        Assert.AreEqual(1u, context.Arena[WarpPortableHeapLayout.LiveObjects]);
        Drain(context);
        Assert.AreEqual(6u, context.Arena[WarpPortableHeapLayout.LiveObjects]);
        for (uint worker = 0; worker < 6; worker++)
        {
            uint[] result = context.Result(worker, context.Dispatch);
            Assert.AreEqual(context.Arena[WarpPortableHeapLayout.Context], result[0]);
            Assert.IsGreaterThan(0u, result[1]);
            Assert.AreEqual(1u, result[2]);
        }
    }

    [TestMethod]
    public void CollectionBeforeAllocatorCreationAcknowledgesBusyAndRetriesWithoutAUserFault()
    {
        WarpCompiledSourceContext context = Create(nameof(Kernels.Identity), 4, 1, [new uint[4], new uint[4], new uint[4]]);
        for (uint worker = 0; worker < 4; worker++) { context.QueueEntryAllocation(worker, ObjectType(context)); }
        WarpCompiledWorkerTicket ticket = context.Claim(0)!;
        Assert.AreEqual(0u, context.RequestCollection());
        context.Execute(ticket);
        context.Commit(ticket);
        for (int attempt = 0; context.Arena[WarpPortableHeapLayout.LeaseState] != 0 && attempt < 10000; attempt++) { context.Advance(0); }
        Assert.AreEqual(1u, context.CooperativeHeapRetries);
        Assert.AreEqual(0u, context.Arena[WarpPortableHeapLayout.LiveObjects]);
        Assert.AreEqual(0u, context.AdvanceCollection());
        Drain(context);
        Assert.AreEqual(WarpPortableSchedulerLayout.CompletedContext, context.State);
        Assert.AreEqual(4u, context.Arena[WarpPortableHeapLayout.LiveObjects]);
        for (uint worker = 0; worker < 4; worker++) { Assert.AreEqual(context.Arena[WarpPortableHeapLayout.Context], context.Result(worker, context.Dispatch)[0]); }
    }

    [TestMethod]
    public void PreciseSourceCallerRootsSurviveCollectionDuringCompiledRecursion()
    {
        WarpCompiledSourceContext context = Create(nameof(Kernels.Nested), 5, 1,
            [new uint[5], new uint[5], new uint[5], Enumerable.Repeat(4u, 5).ToArray()]);
        for (uint worker = 0; worker < 5; worker++) { context.QueueEntryAllocation(worker, ObjectType(context)); }
        for (int attempt = 0; context.MachineState(0)[WarpLogicalMachineLayout.LogicalDepthOffset] < 2 && attempt < 10000; attempt++)
        {
            context.Advance(0);
        }
        Assert.IsGreaterThan(1u, context.MachineState(0)[WarpLogicalMachineLayout.LogicalDepthOffset]);
        uint live = context.Arena[WarpPortableHeapLayout.LiveObjects];
        context.RequestCollection();
        for (int attempt = 0; context.Arena[WarpPortableHeapLayout.LeaseState] != 0 && attempt < 10000; attempt++) { context.Advance(0); }
        Assert.AreEqual(0u, context.AdvanceCollection());
        Assert.IsGreaterThanOrEqualTo(live, context.Arena[WarpPortableHeapLayout.LiveObjects]);
        Assert.IsLessThanOrEqualTo(live + 1, context.Arena[WarpPortableHeapLayout.LiveObjects]);
        Drain(context);
        for (uint worker = 0; worker < 5; worker++) { Assert.AreEqual(context.Arena[WarpPortableHeapLayout.Context], context.Result(worker, context.Dispatch)[0]); }
    }

    [TestMethod]
    public void CancellationDrainsOwnedGeneratedHelperThenDisposesReservedRoots()
    {
        WarpCompiledSourceContext context = Create(nameof(Kernels.Identity), 7, 1, [new uint[7], new uint[7], new uint[7]]);
        for (uint worker = 0; worker < 7; worker++) { context.QueueEntryAllocation(worker, ObjectType(context)); }
        context.Advance(0);
        Assert.AreEqual(1u, context.Arena[WarpPortableHeapLayout.LeaseState]);
        context.RequestCancellation();
        Drain(context);
        Assert.AreEqual(WarpPortableSchedulerLayout.CancelledContext, context.State);
        Assert.AreEqual(0u, context.Arena[WarpPortableHeapLayout.LeaseState]);
        Assert.AreEqual(0u, context.Arena[WarpPortableHeapLayout.PendingResult]);
        Assert.ThrowsExactly<InvalidOperationException>(() => context.Result(0, context.Dispatch));
        context.RequestDisposal();
        Assert.AreEqual(0u, context.FinishDisposal());
        Assert.AreEqual(WarpPortableSchedulerLayout.DisposedContext, context.State);
        for (uint root = 0; root < context.Plan.Workers * context.Arena[context.Scheduler + WarpPortableSchedulerLayout.RootSlotsPerWorker]; root++)
        {
            uint entry = context.Arena[WarpPortableHeapLayout.RootStart] + root * WarpPortableHeapLayout.RootWords;
            Assert.AreEqual(WarpPortableHeapLayout.RuntimeOwnedRoot, context.Arena[entry + WarpPortableHeapLayout.RootOwnership]);
            CollectionAssert.AreEqual(new uint[] { 0, 0, 0 }, context.Arena.Skip((int)(entry + WarpPortableHeapLayout.RootReference)).Take(3).ToArray());
        }
    }

    [TestMethod]
    public void RedispatchRetainsOwnedOutputsAndReclaimsOnlyAfterExactGenerationRelease()
    {
        WarpCompiledSourceContext context = Create(nameof(Kernels.Identity), 4, 1, [new uint[4], new uint[4], new uint[4]]);
        for (uint worker = 0; worker < 4; worker++) { context.QueueEntryAllocation(worker, ObjectType(context)); }
        Drain(context);
        uint oldDispatch = context.Dispatch;
        context.RequestCollection();
        Assert.AreEqual(0u, context.AdvanceCollection());
        Assert.AreEqual(4u, context.Arena[WarpPortableHeapLayout.LiveObjects]);
        Assert.AreEqual(0u, context.BeginDispatch([new uint[4], new uint[4], new uint[4]]));
        for (int attempt = 0; attempt < 32; attempt++) { context.Advance(0); }
        for (uint worker = 0; worker < 4; worker++)
        {
            Assert.AreEqual(WarpPortableSchedulerLayout.WaitingOutput, context.WorkerState(worker));
            Assert.AreEqual(WarpPortableSchedulerLayout.Invalid, context.ReleaseOutput(worker, context.Dispatch));
        }
        context.RequestCollection();
        Assert.AreEqual(0u, context.AdvanceCollection());
        Assert.AreEqual(4u, context.Arena[WarpPortableHeapLayout.LiveObjects]);
        for (uint worker = 0; worker < 4; worker++) { Assert.AreEqual(0u, context.ReleaseOutput(worker, oldDispatch)); }
        Drain(context);
        for (uint worker = 0; worker < 4; worker++) { CollectionAssert.AreEqual(new uint[] { 0, 0, 0 }, context.Result(worker, context.Dispatch)); }
        context.RequestCollection();
        Assert.AreEqual(0u, context.AdvanceCollection());
        Assert.AreEqual(0u, context.Arena[WarpPortableHeapLayout.LiveObjects]);
    }
}
