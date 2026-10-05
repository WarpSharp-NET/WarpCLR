using System.Reflection.Emit;
using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

internal static partial class WarpPortableClosedFrameExceptionCases
{
    internal static void ActualConstructorOwnersSurviveRealFilterSearchAndRetireAfterUnwind()
    {
        foreach (int quantum in new[] { 31, 4096 })
        foreach (uint decision in new uint[] { 0, 1 })
        {
            var driver = new WarpPortableClosedFrameExceptionDriver(nameof(Sources.FilterPair), quantum, decision);
            driver.ExecuteUntil(() => AtOriginalThrowBoundary(driver));
            AssertTemporaryOwners(driver, driver.Owner, driver.Owner);
            driver.ExecuteUntil(() => IsPhaseBoundary(driver, WarpPortableExceptionLayout.Filtering));
            uint record = driver.ActiveRecord;
            AssertTemporaryOwners(driver, driver.Owner, driver.Owner);
            Assert.AreEqual(0u, driver.Arena[record + WarpPortableExceptionLayout.TemporaryOwnersRetired]);
            Assert.IsGreaterThanOrEqualTo(2u, driver.Arena[record + WarpPortableExceptionLayout.FrameCount]);
            driver.ExecuteUntil(() => IsPhaseBoundary(driver, WarpPortableExceptionLayout.Caught));
            AssertTemporaryOwners(driver, [0, 0, 0], [0, 0, 0]);
            Assert.AreEqual(1u, driver.Arena[record + WarpPortableExceptionLayout.TemporaryOwnersRetired]);
            driver.Execute();
            Assert.AreEqual(WarpLogicalMachineLayout.Completed, driver.State[WarpLogicalMachineLayout.StatusOffset]);
        }
    }

    internal static void ARealHandledConstructorCalleeKeepsItsSuspendedOriginalNewobjOwners()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            var driver = new WarpPortableClosedFrameExceptionDriver(nameof(Sources.ConstructorHandlesItsCallee), quantum);
            driver.ExecuteUntil(() => AtOriginalThrowBoundary(driver));
            AssertTemporaryOwners(driver, driver.Owner, [0, 0, 0]);
            driver.ExecuteUntil(() => IsPhaseBoundary(driver, WarpPortableExceptionLayout.Caught));
            AssertTemporaryOwners(driver, driver.Owner, [0, 0, 0]);
            driver.Execute();
            Assert.AreEqual(WarpLogicalMachineLayout.Completed, driver.State[WarpLogicalMachineLayout.StatusOffset]);
            foreach (int offset in driver.Program.Lowered.EntryProjection.ResultType.ManagedRootByteOffsets)
            {
                CollectionAssert.AreEqual(driver.Owner, Enumerable.Range(offset / 4, 3).Select(word =>
                    driver.State[driver.Program.Layout.GetResultWordOffset(word, WarpPortableClosedFrameExceptionDriver.MaximumDepth)]).ToArray());
            }
        }
    }

    internal static void AnActualEscapingConstructorPublishesTheOriginalTypedExceptionReference()
    {
        foreach (int quantum in new[] { 31, 4096 })
        {
            var driver = new WarpPortableClosedFrameExceptionDriver(nameof(Sources.ConstructorEscapes), quantum);
            driver.Execute();
            Assert.AreEqual(WarpLogicalMachineLayout.Faulted, driver.State[WarpLogicalMachineLayout.StatusOffset]);
            Assert.AreEqual(8u, driver.State[WarpLogicalMachineLayout.FaultKindOffset]);
            CollectionAssert.AreEqual(driver.Owner,
                driver.State.AsSpan(WarpLogicalMachineLayout.EscapedExceptionContextOffset, 3).ToArray());
            Assert.AreEqual(driver.Owner[2], driver.Arena[driver.Arena[WarpPortableHeapLayout.SlotStart] +
                (driver.Owner[1] - 1) * WarpPortableHeapLayout.SlotWords + WarpPortableHeapLayout.SlotGeneration]);
        }
    }

    private static bool AtOriginalThrowBoundary(WarpPortableClosedFrameExceptionDriver driver)
    {
        if (driver.State[WarpLogicalMachineLayout.SourceBoundaryStateOffset] != WarpLogicalMachineLayout.BeforeSourceBoundary) { return false; }
        int frame = WarpLogicalMachineLayout.HeaderWords + checked((int)driver.State[WarpLogicalMachineLayout.DepthOffset] - 1) * driver.Program.Layout.FrameWords;
        int function = checked((int)driver.State[frame]);
        if (function == 0 || driver.Program.Layout.IsRuntimeHelper(function)) { return false; }
        int pc = checked((int)driver.State[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset]);
        WarpPortableWordBody body = driver.Program.Lowered.Bodies.First(body => body.Function == function);
        int block = driver.Program.Layout.Nodes[pc].Block;
        return body.SourceBlocks.Any(source => source.Block == block && source.Instruction.OpCode == OpCodes.Throw.Value);
    }

    private static bool IsPhaseBoundary(WarpPortableClosedFrameExceptionDriver driver, uint phase)
    {
        if (driver.State[WarpLogicalMachineLayout.SourceBoundaryStateOffset] != WarpLogicalMachineLayout.BeforeSourceBoundary) { return false; }
        // Entering a catch restores ActiveRecord to the parent while the caught
        // scope retains its own record. Observe that immutable worker row range,
        // rather than requiring the caught scope to stay the active search.
        uint start = driver.Descriptor + driver.Arena[driver.Descriptor + WarpPortableExceptionLayout.RecordStart];
        uint count = driver.Arena[driver.Descriptor + WarpPortableExceptionLayout.RecordsPerWorker];
        for (uint index = 0; index < count; index++)
        {
            if (driver.Arena[start + index * WarpPortableExceptionLayout.RecordWords + WarpPortableExceptionLayout.Phase] == phase) { return true; }
        }
        return false;
    }

    private static void AssertTemporaryOwners(WarpPortableClosedFrameExceptionDriver driver, params uint[][] expected)
    {
        WarpPortableWordBody body = driver.Program.Lowered.Bodies.First(body =>
            string.Equals(body.MethodIdentity, driver.Program.Graph.EntryIdentity, StringComparison.Ordinal));
        Assert.HasCount(1, body.PrivateTemporaries);
        WarpPortableWordPrivateTemporary temporary = body.PrivateTemporaries[0];
        Assert.HasCount(expected.Length, temporary.Owners);
        int frame = -1;
        for (uint physical = 0; physical < driver.State[WarpLogicalMachineLayout.DepthOffset]; physical++)
        {
            int candidate = WarpLogicalMachineLayout.HeaderWords + checked((int)physical) * driver.Program.Layout.FrameWords;
            if (driver.State[candidate + WarpLogicalMachineLayout.FrameFunctionOffset] == (uint)body.Function) { frame = candidate; break; }
        }
        Assert.IsGreaterThanOrEqualTo(0, frame);
        for (int index = 0; index < temporary.Owners.Length; index++)
        {
            CollectionAssert.AreEqual(expected[index], driver.State.AsSpan(frame + driver.Program.Layout.PrivateOffset +
                temporary.Owners[index].PrivateWordOffset, 3).ToArray());
        }
    }
}
