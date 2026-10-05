using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableExceptionSourceDriver
{
    internal void PublishCompletedResult()
    {
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, State[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(1u, Arena[Scheduler + WarpPortableSchedulerLayout.WorkerCount]);
        uint destination = Arena[ScheduledWorker + WarpPortableSchedulerLayout.LogicalStackBase];
        int words = Program.Lowered.EntryProjection.ResultType.WordCount;
        for (int word = 0; word < words; word++) { Arena[destination + (uint)word] = State[Program.Layout.GetResultWordOffset(word, 256)]; }
        Assert.AreEqual(0u, Scheduled(nameof(WarpPortableSchedulerServices.ReleaseHeapService), LogicalWorker, RunGeneration));
        Assert.AreEqual(0u, Scheduled(nameof(WarpPortableSchedulerServices.PublishOutputRoots), LogicalWorker, RunGeneration, DispatchGeneration));
        Assert.AreEqual(0u, Scheduled(nameof(WarpPortableSchedulerServices.AcknowledgeOutputRoots), LogicalWorker, RunGeneration, DispatchGeneration));
        Assert.AreEqual(0u, Scheduled(nameof(WarpPortableSchedulerServices.CompleteWorker), LogicalWorker, RunGeneration));
        Assert.AreEqual(WarpPortableSchedulerLayout.CompletedContext, Arena[Scheduler + WarpPortableSchedulerLayout.ContextState]);
    }

    internal void CollectCompleted()
    {
        Assert.AreEqual(WarpPortableSchedulerLayout.CompletedContext, Arena[Scheduler + WarpPortableSchedulerLayout.ContextState]);
        Assert.AreEqual(0u, Scheduled(nameof(WarpPortableSchedulerServices.RequestCollection)));
        uint epoch = Arena[Scheduler + WarpPortableSchedulerLayout.GCEpoch];
        Assert.AreEqual(0u, Scheduled(nameof(WarpPortableSchedulerServices.BeginCollection), epoch));
        uint status = Heap(nameof(WarpPortableHeapServices.Collect));
        Assert.AreEqual(0u, status);
        Assert.AreEqual(0u, Scheduled(nameof(WarpPortableSchedulerServices.FinishCollection), epoch, status));
    }
}
