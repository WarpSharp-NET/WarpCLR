using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableExceptionTests
{
    [TestMethod]
    public void ActualCaughtScopeRootsAreReleasedWhileAManagedReturnTupleIsCopiedAtomically()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            var driver = new WarpPortableExceptionSourceDriver(nameof(WarpPortableExceptionFixtureSources.ReturnException), quantum);
            driver.Execute();
            Assert.AreEqual(WarpLogicalMachineLayout.Completed, driver.State[WarpLogicalMachineLayout.StatusOffset], driver.Diagnostic());
            uint[] returned = Enumerable.Range(0, 3).Select(word => driver.State[driver.Program.Layout.GetResultWordOffset(word, 256)]).ToArray();
            CollectionAssert.AreEqual(driver.Owner, returned);
            for (uint index = 0; index < driver.Arena[driver.Descriptor + WarpPortableExceptionLayout.RecordsPerWorker]; index++)
            {
                uint record = driver.Descriptor + driver.Arena[driver.Descriptor + WarpPortableExceptionLayout.RecordStart] + index * WarpPortableExceptionLayout.RecordWords;
                Assert.AreEqual(WarpPortableExceptionLayout.Free, driver.Arena[record + WarpPortableExceptionLayout.Phase]);
                uint root = driver.Arena[record + WarpPortableExceptionLayout.RecordRoot];
                uint row = driver.Arena[WarpPortableHeapLayout.RootStart] + (root - 1) * WarpPortableHeapLayout.RootWords;
                CollectionAssert.AreEqual(new uint[] { 0, 0, 0 }, driver.Arena.AsSpan((int)(row + WarpPortableHeapLayout.RootReference), 3).ToArray());
            }
            driver.PublishCompletedResult();
            driver.CollectCompleted();
            uint slot = driver.Arena[WarpPortableHeapLayout.SlotStart] + (driver.Owner[1] - 1) * WarpPortableHeapLayout.SlotWords;
            Assert.AreEqual(WarpPortableHeapLayout.Allocated, driver.Arena[slot + WarpPortableHeapLayout.SlotState]);
            Assert.AreEqual(driver.Owner[2], driver.Arena[slot + WarpPortableHeapLayout.SlotGeneration]);
            Assert.AreEqual(0u, driver.Scheduled(nameof(WarpPortableSchedulerServices.ReleaseOutputRoots), driver.LogicalWorker, driver.DispatchGeneration));
            driver.CollectCompleted();
            Assert.AreEqual(WarpPortableHeapLayout.Free, driver.Arena[slot + WarpPortableHeapLayout.SlotState]);
        }
    }

    [TestMethod]
    public void ActualLoweredSourceThrowsAndCatchesAPrevalidatedNonNullExceptionAtExactBoundaries()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            foreach ((Type type, uint expected) in new[] { (typeof(InvalidOperationException), 17u), (typeof(StackOverflowException), 29u) })
            {
                var driver = new WarpPortableExceptionSourceDriver(nameof(WarpPortableExceptionFixtureSources.Catch), quantum, type);
                driver.Execute();
                Assert.AreEqual(WarpLogicalMachineLayout.Completed, driver.State[WarpLogicalMachineLayout.StatusOffset], $"fault {driver.State[1]}");
                Assert.AreEqual(expected, driver.State[WarpLogicalMachineLayout.ResultOffset]);
                Assert.IsGreaterThan(3u, driver.Boundaries);
                Assert.AreEqual(1u, driver.Arena[driver.Descriptor + WarpPortableExceptionLayout.NextTrace]);
                Assert.AreEqual(driver.Program.Plan.PlanHash, driver.Program.Lowered.ExecutionPlanHash, StringComparer.Ordinal);
                Assert.AreEqual(driver.Program.Plan.TypeSchemaHash, driver.Program.Schema.SchemaHash, StringComparer.Ordinal);
            }
        }
    }

    [TestMethod]
    public void ActualLoweredSourceCallerAndRethrowPreserveOriginalObjectAndTrace()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            foreach ((string method, uint expected) in new[] { (nameof(WarpPortableExceptionFixtureSources.Caller), 61u), (nameof(WarpPortableExceptionFixtureSources.Rethrow), 43u) })
            {
                var driver = new WarpPortableExceptionSourceDriver(method, quantum); driver.Execute();
                Assert.AreEqual(WarpLogicalMachineLayout.Completed, driver.State[WarpLogicalMachineLayout.StatusOffset], $"fault {driver.State[1]}");
                Assert.AreEqual(expected, driver.State[WarpLogicalMachineLayout.ResultOffset]);
                Assert.AreEqual(1u, driver.Arena[driver.Descriptor + WarpPortableExceptionLayout.NextTrace]);
                uint slot = driver.Arena[WarpPortableHeapLayout.SlotStart] + (driver.Owner[1] - 1) * WarpPortableHeapLayout.SlotWords;
                uint payload = driver.Arena[slot + WarpPortableHeapLayout.SlotPayload];
                Assert.AreEqual(1u, driver.Arena[payload + WarpPortableSourceExceptionLayout.TraceIdentityWord]);
                Assert.IsGreaterThan(5u, driver.Boundaries);
            }
        }
    }

    [TestMethod]
    public void ActualLoweredSourceEscapesThroughTerminal8BeforeAnyNormalResultPublication()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            var driver = new WarpPortableExceptionSourceDriver(nameof(WarpPortableExceptionFixtureSources.Escape), quantum); driver.Execute();
            Assert.AreEqual(WarpLogicalMachineLayout.Faulted, driver.State[WarpLogicalMachineLayout.StatusOffset]);
            Assert.AreEqual(WarpLogicalMachineLayout.ManagedExceptionFault, driver.State[WarpLogicalMachineLayout.FaultKindOffset]);
            CollectionAssert.AreEqual(driver.Owner, driver.State.AsSpan(WarpLogicalMachineLayout.EscapedExceptionContextOffset, 3).ToArray());
            uint index = driver.Arena[driver.Worker + WarpPortableExceptionLayout.ActiveRecord];
            uint record = driver.Descriptor + driver.Arena[driver.Descriptor + WarpPortableExceptionLayout.RecordStart] + (index - 1) * WarpPortableExceptionLayout.RecordWords;
            Assert.AreEqual(1u, driver.Arena[record + WarpPortableExceptionLayout.TerminalCommitted]);
            Assert.AreEqual(WarpPortableSchedulerLayout.DispatchQuarantined, driver.Service(nameof(WarpPortableExceptionServices.PublishEscapedFault),
                driver.Arena[record + WarpPortableExceptionLayout.RaiseGeneration]));
            Assert.AreEqual(1u, driver.Arena[driver.Scheduler + WarpPortableSchedulerLayout.OutputQuarantined]);
        }
    }

    [TestMethod]
    public void ActualLoweredSourceFinallyAndLeaveExecuteTheirCapturedInstructions()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            foreach ((string method, uint expected, uint traces) in new[]
                { (nameof(WarpPortableExceptionFixtureSources.Finally), 71u, 1u), (nameof(WarpPortableExceptionFixtureSources.Leave), 53u, 0u) })
            {
                var driver = new WarpPortableExceptionSourceDriver(method, quantum); driver.Execute();
                Assert.AreEqual(WarpLogicalMachineLayout.Completed, driver.State[WarpLogicalMachineLayout.StatusOffset], $"fault {driver.State[1]}");
                Assert.AreEqual(expected, driver.State[WarpLogicalMachineLayout.ResultOffset]);
                Assert.AreEqual(traces, driver.Arena[driver.Descriptor + WarpPortableExceptionLayout.NextTrace]);
                Assert.IsGreaterThan(5u, driver.Boundaries);
            }
        }
    }

    [TestMethod]
    public void ActualLoweredSourceFinallyReplacementUsesTheNewObjectAndActualThrowSite()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            var driver = new WarpPortableExceptionSourceDriver(nameof(WarpPortableExceptionFixtureSources.Replace), quantum,
                replacementType: typeof(StackOverflowException)); driver.Execute();
            Assert.AreEqual(WarpLogicalMachineLayout.Completed, driver.State[WarpLogicalMachineLayout.StatusOffset], $"fault {driver.State[1]}");
            Assert.AreEqual(59u, driver.State[WarpLogicalMachineLayout.ResultOffset]);
            Assert.AreEqual(2u, driver.Arena[driver.Descriptor + WarpPortableExceptionLayout.NextTrace]);
            uint slot = driver.Arena[WarpPortableHeapLayout.SlotStart] + (driver.SecondOwner[1] - 1) * WarpPortableHeapLayout.SlotWords;
            uint payload = driver.Arena[slot + WarpPortableHeapLayout.SlotPayload];
            Assert.AreEqual(2u, driver.Arena[payload + WarpPortableSourceExceptionLayout.TraceIdentityWord]);
            var source = driver.Program.Graph.Methods.First(method => string.Equals(method.SourceMethod.Name, nameof(WarpPortableExceptionFixtureSources.Replace), StringComparison.Ordinal));
            uint site = checked((uint)source.Instructions.Last(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Throw).Offset);
            Assert.AreEqual(site, driver.Arena[payload + WarpPortableSourceExceptionLayout.ThrowOffsetWord]);
        }
    }

    [TestMethod]
    public void ActualLoweredSourceFilterReadsOriginalLocalsBeforeFinallyUnwinds()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            foreach ((uint decision, uint expected) in new[] { (0u, 37u), (1u, 31u) })
            {
                var driver = new WarpPortableExceptionSourceDriver(nameof(WarpPortableExceptionFixtureSources.SearchBeforeUnwind), quantum, flag: decision);
                driver.Execute();
                Assert.AreEqual(WarpLogicalMachineLayout.Completed, driver.State[WarpLogicalMachineLayout.StatusOffset], driver.Diagnostic());
                Assert.AreEqual(expected, driver.State[WarpLogicalMachineLayout.ResultOffset]);
                Assert.AreEqual(1u, driver.Arena[driver.Descriptor + WarpPortableExceptionLayout.NextTrace]);
                Assert.IsTrue(driver.Program.Lowered.Bodies.Any(body => body.AliasOwnerFunction != -1));
            }
        }
    }

    [TestMethod]
    public void ActualLoweredSourceFilterEscapeRejectsTheFilterAndPreservesTheOriginalException()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            var driver = new WarpPortableExceptionSourceDriver(nameof(WarpPortableExceptionFixtureSources.FilterEscape), quantum,
                replacementType: typeof(StackOverflowException)); driver.Execute();
            Assert.AreEqual(WarpLogicalMachineLayout.Completed, driver.State[WarpLogicalMachineLayout.StatusOffset], driver.Diagnostic());
            Assert.AreEqual(83u, driver.State[WarpLogicalMachineLayout.ResultOffset]);
            Assert.AreEqual(2u, driver.Arena[driver.Descriptor + WarpPortableExceptionLayout.NextTrace]);
            uint slot = driver.Arena[WarpPortableHeapLayout.SlotStart] + (driver.Owner[1] - 1) * WarpPortableHeapLayout.SlotWords;
            Assert.AreEqual(1u, driver.Arena[driver.Arena[slot + WarpPortableHeapLayout.SlotPayload] + WarpPortableSourceExceptionLayout.TraceIdentityWord]);
        }
    }

    [TestMethod]
    public void ActualLoweredSourceRethrowRestoresItsCaughtTraceAfterAnotherThrowOfTheSameObject()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            var driver = new WarpPortableExceptionSourceDriver(nameof(WarpPortableExceptionFixtureSources.RepeatedObjectRethrow), quantum); driver.Execute();
            Assert.AreEqual(WarpLogicalMachineLayout.Completed, driver.State[WarpLogicalMachineLayout.StatusOffset], driver.Diagnostic());
            Assert.AreEqual(89u, driver.State[WarpLogicalMachineLayout.ResultOffset]);
            Assert.AreEqual(2u, driver.Arena[driver.Descriptor + WarpPortableExceptionLayout.NextTrace]);
            uint slot = driver.Arena[WarpPortableHeapLayout.SlotStart] + (driver.Owner[1] - 1) * WarpPortableHeapLayout.SlotWords;
            uint payload = driver.Arena[slot + WarpPortableHeapLayout.SlotPayload];
            Assert.AreEqual(1u, driver.Arena[payload + WarpPortableSourceExceptionLayout.TraceIdentityWord]);
            uint firstThrow = (uint)driver.Program.Graph.Methods.First().Instructions.First(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Throw).Offset;
            Assert.AreEqual(firstThrow, driver.Arena[payload + WarpPortableSourceExceptionLayout.ThrowOffsetWord]);
        }
    }
}
