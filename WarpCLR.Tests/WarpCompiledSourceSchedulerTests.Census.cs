using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpCompiledSourceSchedulerTests
{
    [TestMethod]
    [DataRow(33, 3)]
    [DataRow(131, 7)]
    public void CompiledStoppedCensusDrainsEveryParticipantAndPendingHelperBeyondBindingLimit(int participants, int residency)
    {
        uint workers = checked((uint)participants);
        uint residents = checked((uint)residency);
        WarpCompiledSourceContext context = Create(nameof(Kernels.Identity), workers, residents,
            [new uint[workers], new uint[workers], new uint[workers]], maximumDepth: 4);
        context.QueueEntryAllocation(0, ObjectType(context));
        WarpCompiledWorkerTicket? current = context.Claim(0);
        Assert.IsNotNull(current);
        for (uint resident = 1; resident < residents; resident++) { Assert.IsNotNull(context.Claim(resident)); }
        WarpCompiledWorkerTicket? pending = null;
        for (int attempt = 0; pending is null && attempt < 100000; attempt++)
        {
            WarpCompiledWorkerTicket? ticket = current ?? context.Claim(0);
            current = null;
            if (ticket is null) { continue; }
            context.Execute(ticket);
            if (context.Arena[WarpPortableHeapLayout.PendingResult] != 0) { pending = ticket; }
            else { context.Commit(ticket); }
        }
        Assert.IsNotNull(pending);
        Assert.AreEqual(residents, context.Arena[context.Scheduler + WarpPortableSchedulerLayout.RunningCount]);
        WarpCompiledControllerGrant? grant = residency == 7 ? context.Controller.TryAcquire() : null;
        WarpCompiledPausedCensus census = context.CapturePausedCensus(grant)!;
        Assert.HasCount(participants, census.Workers);
        Assert.IsLessThan(32, context.Plan.Services.CleanupKernels.Count);
        Assert.AreEqual(0u, context.QuarantineStoppedExecutionFailure(pending, census));
        Assert.AreEqual(WarpPortableSchedulerLayout.DisposedContext, context.State);
        Assert.AreEqual(0u, context.Arena[context.Scheduler + WarpPortableSchedulerLayout.RunningCount]);
        Assert.AreEqual(0u, context.Arena[context.Scheduler + WarpPortableSchedulerLayout.ControllerOwner]);
        Assert.AreEqual(WarpPortableSchedulerLayout.NoWorker, context.Arena[context.Scheduler + WarpPortableSchedulerLayout.ServiceOwner]);
        Assert.AreEqual(0u, context.Arena[WarpPortableHeapLayout.LeaseState]);
        Assert.AreEqual(0u, context.Arena[WarpPortableHeapLayout.PendingResult]);
        Assert.AreEqual(1u, context.Arena[context.Scheduler + WarpPortableSchedulerLayout.OutputQuarantined]);
        for (uint worker = 0; worker < workers; worker++) { Assert.AreEqual(WarpPortableSchedulerLayout.Disposed, context.WorkerState(worker)); }
        Assert.ThrowsExactly<InvalidOperationException>(() => context.Execute(pending));
        Assert.ThrowsExactly<InvalidOperationException>(() => context.Result(0, context.Dispatch));
        Assert.ThrowsExactly<InvalidOperationException>(() => context.DisposePausedCensus(census, pending));
        Assert.IsNull(context.Claim(0));
    }

    [TestMethod]
    public void PausedCensusRejectsAnActiveReservationAndStaleExactWorkerProgress()
    {
        WarpCompiledSourceContext context = Create(nameof(Kernels.Loop), 33, 3, [Enumerable.Repeat(4u, 33).ToArray()]);
        WarpCompiledWorkerTicket ticket = context.Claim(0)!;
        ticket.BeginExecution();
        Assert.ThrowsExactly<InvalidOperationException>(() => context.CapturePausedCensus());
        ticket.FinishExecution();
        WarpCompiledPausedCensus paused = context.CapturePausedCensus()!;
        context.Advance(1);
        uint[] rows = context.Arena.Skip(checked((int)(context.Scheduler + context.Arena[context.Scheduler + WarpPortableSchedulerLayout.WorkerStart]))).Take(33 * 64).ToArray();
        Assert.ThrowsExactly<InvalidOperationException>(() => context.DisposePausedCensus(paused, ticket));
        CollectionAssert.AreEqual(rows, context.Arena.Skip(checked((int)(context.Scheduler + context.Arena[context.Scheduler + WarpPortableSchedulerLayout.WorkerStart]))).Take(33 * 64).ToArray());
        Assert.AreEqual(WarpPortableSchedulerLayout.Active, context.State);
        Assert.AreEqual(0u, context.DisposePausedCensus(context.CapturePausedCensus()!, ticket));
    }

    [TestMethod]
    public void TypedPrivateOwnerWriteUsesServiceLoanWithoutManagedArenaOpcodes()
    {
        WarpCompiledSourceContext context = Create(nameof(Kernels.SelectOwner), 5, 1,
            [new uint[5], new uint[5], new uint[5], new uint[5], new uint[5], new uint[5], [0, 1, 0, 1, 0]]);
        Assert.IsFalse(context.Plan.Layout.RequiresManagedMemory);
        WarpCompiledWorkerTicket? owner = null;
        for (int attempt = 0; owner is null && attempt < 10000; attempt++)
        {
            WarpCompiledWorkerTicket? ticket = context.Claim(0);
            if (ticket is null) { continue; }
            if (context.Arena[WarpPortableHeapLayout.LeaseState] != 0) { owner = ticket; }
            else { context.Execute(ticket); context.Commit(ticket); }
        }
        Assert.IsNotNull(owner);
        Assert.IsTrue(context.Plan.RequiresSourceLoan(context.MachineState(owner.Worker).ToArray()));
        context.RequestCollection();
        Assert.AreEqual(WarpPortableSchedulerLayout.NeedGCParking, context.AdvanceCollection());
        context.Execute(owner);
        context.Commit(owner);
        Assert.AreEqual(0u, context.Arena[WarpPortableHeapLayout.LeaseState]);
        Assert.AreEqual(0u, context.AdvanceCollection());
        Drain(context);
        Assert.AreEqual(WarpPortableSchedulerLayout.CompletedContext, context.State);
    }
}
