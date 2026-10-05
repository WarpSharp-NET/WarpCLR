using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableExceptionTests
{
    [TestMethod]
    public void ANewRunningGenerationDoesNotByItselfAuthorizeAnOwnedEhContinuation()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            var driver = new WarpPortableExceptionTestDriver(nameof(WarpPortableExceptionFixtureSources.Catch), quantum: quantum);
            Assert.AreEqual(0u, driver.RaiseOwner());
            uint originalRun = driver.Arena[driver.Worker + WarpPortableExceptionLayout.RunGeneration];
            Assert.AreEqual(0u, driver.Scheduled(nameof(WarpPortableSchedulerServices.YieldWorker), 0, originalRun));
            Assert.AreEqual(0u, driver.Scheduled(nameof(WarpPortableSchedulerServices.TryAcquireWorker), 0));
            Assert.AreEqual(originalRun + 1, driver.Run);
            uint[] before = (uint[])driver.Arena.Clone();
            Assert.AreEqual(WarpPortableExceptionLayout.InvalidTicket, driver.Service(nameof(WarpPortableExceptionServices.AdvanceSearch), driver.Raise));
            CollectionAssert.AreEqual(before, driver.Arena);
            Assert.AreEqual(originalRun, driver.Arena[driver.Worker + WarpPortableExceptionLayout.RunGeneration]);
        }
    }

    [TestMethod]
    public void ARequestedGcEpochDoesNotMintEhContinuationTransitionAuthority()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            var driver = new WarpPortableExceptionTestDriver(nameof(WarpPortableExceptionFixtureSources.Catch), quantum: quantum);
            Assert.AreEqual(0u, driver.RaiseOwner());
            uint epoch = driver.Arena[driver.Worker + WarpPortableExceptionLayout.CollectionEpoch];
            Assert.AreEqual(0u, driver.Scheduled(nameof(WarpPortableSchedulerServices.RequestCollection)));
            Assert.AreEqual(epoch + 1, driver.Arena[driver.Scheduler + WarpPortableSchedulerLayout.GCEpoch]);
            uint[] before = (uint[])driver.Arena.Clone();
            Assert.AreEqual(WarpPortableExceptionLayout.InvalidTicket, driver.Service(nameof(WarpPortableExceptionServices.AdvanceSearch), driver.Raise));
            CollectionAssert.AreEqual(before, driver.Arena);
            Assert.AreEqual(epoch, driver.Arena[driver.Worker + WarpPortableExceptionLayout.CollectionEpoch]);
        }
    }

    [TestMethod]
    public void GeneratedRaiseRejectsStaleOwnersAndImplicitFactorySubstitutesBeforeMutation()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            var driver = new WarpPortableExceptionTestDriver(nameof(WarpPortableExceptionFixtureSources.Escape), quantum: quantum);
            foreach (uint[] invalid in new[] { new uint[] { 0, 0, 0 }, new[] { driver.Owner[0] + 1, driver.Owner[1], driver.Owner[2] },
                new[] { driver.Owner[0], driver.Owner[1], driver.Owner[2] + 1 } })
            {
                uint[] before = (uint[])driver.Arena.Clone();
                Assert.AreEqual(WarpPortableExceptionLayout.InvalidReference, driver.RaiseReference(invalid, driver.ExceptionType));
                CollectionAssert.AreEqual(before, driver.Arena);
            }
            uint[] unchanged = (uint[])driver.Arena.Clone();
            Assert.AreEqual(WarpPortableExceptionLayout.NeedsFaultFactory, driver.Service(nameof(WarpPortableExceptionServices.RaisePreparedImplicit), 1));
            CollectionAssert.AreEqual(unchanged, driver.Arena);
            driver.Arena[driver.Descriptor + WarpPortableExceptionLayout.NextTrace] = uint.MaxValue;
            unchanged = (uint[])driver.Arena.Clone();
            Assert.AreEqual(WarpPortableExceptionLayout.GenerationExhausted, driver.RaiseOwner());
            CollectionAssert.AreEqual(unchanged, driver.Arena);
        }
    }

    [TestMethod]
    public void GeneratedRaiseRequiresTheExactControllerTicketAndReservedRootOwnership()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            var driver = new WarpPortableExceptionTestDriver(nameof(WarpPortableExceptionFixtureSources.Escape), quantum: quantum);
            uint[] before = (uint[])driver.Arena.Clone();
            Assert.AreEqual(WarpPortableExceptionLayout.InvalidTicket, WarpPortableExceptionTestService.Run(typeof(WarpPortableExceptionServices),
                nameof(WarpPortableExceptionServices.DiscardPrepared), driver.Arena,
                [WarpPortableExceptionTestBuilder.Controller + 1, 0, driver.Run, 1], quantum));
            CollectionAssert.AreEqual(before, driver.Arena);
            uint root = driver.Arena[driver.Prepared + WarpPortableExceptionLayout.RecordRoot];
            uint row = driver.Arena[WarpPortableHeapLayout.RootStart] + (root - 1) * WarpPortableHeapLayout.RootWords;
            driver.Arena[row + WarpPortableHeapLayout.RootOwnership] = WarpPortableHeapLayout.HostOwnedRoot;
            before = (uint[])driver.Arena.Clone();
            Assert.AreEqual(WarpPortableExceptionLayout.InvalidDescriptor, driver.RaiseOwner());
            CollectionAssert.AreEqual(before, driver.Arena);
        }
    }

    [TestMethod]
    public void GeneratedResourceTerminationBypassesCatchSearchWhileAnExplicitStackOverflowObjectIsCatchable()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            var explicitThrow = new WarpPortableExceptionTestDriver(nameof(WarpPortableExceptionFixtureSources.Catch), typeof(StackOverflowException), quantum);
            Assert.AreEqual(0u, explicitThrow.RaiseOwner());
            Assert.AreEqual(0u, explicitThrow.Service(nameof(WarpPortableExceptionServices.AdvanceSearch), explicitThrow.Raise));
            uint selected = explicitThrow.Clause(explicitThrow.Arena[explicitThrow.Active + WarpPortableExceptionLayout.SelectedClause]);
            Assert.AreEqual(explicitThrow.Id(typeof(Exception)), explicitThrow.Arena[selected + WarpPortableExceptionLayout.CatchType]);
            var budget = new WarpPortableExceptionTestDriver(nameof(WarpPortableExceptionFixtureSources.Catch), quantum: quantum);
            uint preparedIndex = budget.Arena[budget.Worker + WarpPortableExceptionLayout.PreparedRecord];
            uint frame = budget.Captured(preparedIndex, 1);
            uint site = budget.Site(budget.Arena[frame + WarpPortableExceptionLayout.FrameSite]);
            Assert.AreEqual(0u, budget.Service(nameof(WarpPortableExceptionServices.RaiseResource), 0,
                budget.Arena[site + WarpPortableExceptionLayout.SiteOffset], budget.Arena[site + WarpPortableExceptionLayout.SiteOpCode], 2, 33, 0, 32, 0));
            Assert.AreEqual(1u, budget.Arena[budget.Active + WarpPortableExceptionLayout.Uncatchable]);
            Assert.AreEqual(WarpPortableExceptionLayout.ResourceTermination, budget.Arena[budget.Active + WarpPortableExceptionLayout.Action]);
            Assert.AreEqual(budget.Id(typeof(StackOverflowException)), budget.Arena[budget.Active + WarpPortableExceptionLayout.ExceptionExactType]);
            uint[] source = (uint[])budget.State.Clone(); uint[] arena = (uint[])budget.Arena.Clone();
            Assert.AreEqual(WarpPortableExceptionLayout.InvalidPhase, budget.Service(nameof(WarpPortableExceptionServices.AdvanceSearch), budget.Raise));
            CollectionAssert.AreEqual(source, budget.State); CollectionAssert.AreEqual(arena, budget.Arena);
        }
    }

    [TestMethod]
    public void ReservedExceptionRootsRejectGuessedHostAccessAndSurvivePreciseGeneratedCollection()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            var driver = new WarpPortableExceptionTestDriver(nameof(WarpPortableExceptionFixtureSources.Escape), quantum: quantum);
            Assert.AreEqual(0u, driver.RaiseOwner()); uint record = driver.Active;
            uint root = driver.Arena[record + WarpPortableExceptionLayout.RecordRoot];
            uint row = driver.Arena[WarpPortableHeapLayout.RootStart] + (root - 1) * WarpPortableHeapLayout.RootWords;
            uint generation = driver.Arena[row + WarpPortableHeapLayout.RootGeneration];
            uint[] reserved = driver.Arena.AsSpan((int)row, (int)WarpPortableHeapLayout.RootWords).ToArray();
            Assert.AreEqual(WarpPortableHeapLayout.InvalidReference, driver.Heap(nameof(WarpPortableHeapServices.ReadRoot), root, generation));
            CollectionAssert.AreEqual(reserved, driver.Arena.AsSpan((int)row, (int)WarpPortableHeapLayout.RootWords).ToArray());
            Assert.AreEqual(WarpPortableHeapLayout.InvalidReference, driver.Heap(nameof(WarpPortableHeapServices.ReleaseRoot), root, generation));
            CollectionAssert.AreEqual(reserved, driver.Arena.AsSpan((int)row, (int)WarpPortableHeapLayout.RootWords).ToArray());
            Assert.AreEqual(0u, driver.Scheduled(nameof(WarpPortableSchedulerServices.RequestCollection)));
            uint epoch = driver.Arena[driver.Scheduler + WarpPortableSchedulerLayout.GCEpoch];
            Assert.AreEqual(WarpPortableSchedulerLayout.Yield, driver.Scheduled(nameof(WarpPortableSchedulerServices.ParkForCollection), 0, driver.Run, epoch));
            Assert.AreEqual(0u, driver.Scheduled(nameof(WarpPortableSchedulerServices.ReleaseHeapService), 0, driver.Run));
            Assert.AreEqual(0u, driver.Scheduled(nameof(WarpPortableSchedulerServices.PublishRoots), 0, driver.Run, epoch, 0, 1, 0, 0));
            Assert.AreEqual(0u, driver.Scheduled(nameof(WarpPortableSchedulerServices.ParkForCollection), 0, driver.Run, epoch));
            Assert.AreEqual(0u, driver.Scheduled(nameof(WarpPortableSchedulerServices.BeginCollection), epoch));
            Assert.AreEqual(0u, driver.Heap(nameof(WarpPortableHeapServices.Collect)));
            Assert.AreEqual(0u, driver.Arena[WarpPortableHeapLayout.Result]);
            Assert.AreEqual(0u, driver.Scheduled(nameof(WarpPortableSchedulerServices.FinishCollection), epoch, 0));
            uint slot = driver.Arena[WarpPortableHeapLayout.SlotStart] + (driver.Owner[1] - 1) * WarpPortableHeapLayout.SlotWords;
            Assert.AreEqual(WarpPortableHeapLayout.Allocated, driver.Arena[slot + WarpPortableHeapLayout.SlotState]);
            Assert.AreEqual(driver.Owner[2], driver.Arena[slot + WarpPortableHeapLayout.SlotGeneration]);
            CollectionAssert.AreEqual(reserved, driver.Arena.AsSpan((int)row, (int)WarpPortableHeapLayout.RootWords).ToArray());
        }
    }
}
