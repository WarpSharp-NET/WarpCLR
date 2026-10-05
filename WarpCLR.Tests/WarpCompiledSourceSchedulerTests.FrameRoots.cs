using System.Buffers.Binary;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpCompiledSourceSchedulerTests
{
    [TestMethod]
    [DataRow(1)]
    [DataRow(4096)]
    public void ActualCompiledCallerFrameWritesPreservePackedNeighboursAndRawFloatBits(int quantum)
    {
        WarpCompiledSourcePlan plan = CapturedFramePlan("MutatePacked", 5, 2, quantum);
        uint[] low = [0, 1, 0x81234567, 0x80000000, uint.MaxValue];
        WarpCompiledSourceContext context = BindFramePlan(plan, [low]);
        Drain(context);
        for (uint worker = 0; worker < 5; worker++)
        {
            byte[] bytes = new byte[16];
            bytes[0] = 0xAA;
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(1), 0xEEFF);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(3), 0x0123456700000000ul | low[worker]);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(11), 0xDEADBEEF);
            bytes[15] = 0x55;
            uint[] expected = Enumerable.Range(0, 4)
                .Select(word => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(word * 4))).ToArray();
            CollectionAssert.AreEqual(expected, context.Result(worker, context.Dispatch));
        }
    }

    [TestMethod]
    public void ActualRecursiveCallerOwnersKeepTheirIndependentLiveActivations()
    {
        WarpCompiledSourcePlan plan = CapturedFramePlan("RecursiveOwners", 5, 2, 1);
        uint[] counts = [0, 1, 2, 8, 15];
        WarpCompiledSourceContext context = BindFramePlan(plan, [counts]);
        Drain(context);
        for (uint worker = 0; worker < 5; worker++)
        {
            Assert.AreEqual(counts[worker] * (counts[worker] + 1) / 2, context.Result(worker, context.Dispatch)[0]);
        }
    }

    [TestMethod]
    [DataRow("MakeReference")]
    [DataRow("InstallReference")]
    public void ActualFrameReferenceStoresRemainRootedThroughCompiledCollection(string method)
    {
        WarpCompiledSourcePlan plan = CapturedFramePlan(method, 3, 1, 1);
        uint[][] arguments = method is "MakeReference" ?
            [new uint[3], new uint[3], new uint[3], [0x89ABCDEF, 0, uint.MaxValue], [0xFEDCBA98, 0x80000000, 0]] :
            [new uint[3], new uint[3], new uint[3]];
        WarpCompiledSourceContext context = BindFramePlan(plan, arguments);
        for (uint worker = 0; worker < 3; worker++) { context.QueueEntryAllocation(worker, ObjectType(context)); }
        for (int attempt = 0; attempt < 100000 && context.State == WarpPortableSchedulerLayout.Active; attempt++)
        {
            if (attempt % 7 == 0 && context.Epoch < 3) { context.RequestCollection(); }
            context.Advance(0);
        }
        Assert.AreEqual(WarpPortableSchedulerLayout.CompletedContext, context.State);
        Assert.IsGreaterThan(0u, context.Epoch);
        Assert.HasCount(1, plan.Program.EntryProjection.ResultRoots);
        int referenceWord = plan.Program.EntryProjection.ResultRoots[0].ResultWordOffset;
        for (uint worker = 0; worker < 3; worker++)
        {
            uint[] result = context.Result(worker, context.Dispatch);
            Assert.AreEqual(context.Arena[WarpPortableHeapLayout.Context], result[referenceWord]);
            Assert.IsGreaterThan(0u, result[referenceWord + 1]);
            Assert.IsGreaterThan(0u, result[referenceWord + 2]);
            Assert.AreEqual(method is "MakeReference" ? 0x5Au : 0u, result[0] & 255);
        }
    }

    [TestMethod]
    public void NumericFrameCapabilitiesNeverBecomeHeapRootsAndCorruptOwnersFailWithoutMutation()
    {
        WarpCompiledSourcePlan plan = CapturedFramePlan("RecursiveOwners", 1, 1, 1);
        uint[][] inputs = [[8]];
        WarpCompiledSourceContext context = BindFramePlan(plan, inputs);
        uint[]? captured = null;
        int address = -1;
        for (int attempt = 0; attempt < 10000 && captured is null; attempt++)
        {
            context.Advance(0);
            uint[] candidate = context.MachineState(0).ToArray();
            address = LiveFrameCapability(plan, candidate);
            if (address >= 0) { captured = candidate; }
        }
        Assert.IsNotNull(captured);
        uint[] roots = plan.ProjectRoots(captured, inputs, 0);
        Assert.IsTrue(roots.All(word => word == 0));
        for (int part = 0; part < 6; part++)
        {
            uint[] changed = (uint[])captured.Clone();
            changed[address + part] = part == 5 ? uint.MaxValue : unchecked(changed[address + part] + 1);
            uint[] before = (uint[])changed.Clone();
            Assert.ThrowsExactly<InvalidOperationException>(() => plan.ProjectRoots(changed, inputs, 0));
            CollectionAssert.AreEqual(before, changed);
        }
        context.RequestCancellation();
        Drain(context);
        Assert.AreEqual(WarpPortableSchedulerLayout.CancelledContext, context.State);
        context.RequestDisposal();
        Assert.AreEqual(0u, context.FinishDisposal());
        Assert.AreEqual(WarpPortableSchedulerLayout.DisposedContext, context.State);
    }

    private static WarpCompiledSourcePlan CapturedFramePlan(string method, uint workers, uint residents, int quantum)
    {
        WarpPortableClosedFrameSourceCases.Fixture source = WarpPortableClosedFrameSourceCases.Capture(method);
        return new(source.Graph, source.Schema, source.Program, workers, residents, 64, 100000, quantum, Services.Value);
    }

    private static WarpCompiledSourceContext BindFramePlan(WarpCompiledSourcePlan plan, uint[][] arguments)
    {
        WarpCompiledSourceEvidence.Capture(plan);
        uint[] heap = plan.CreateUnusedHeap(WarpLogicalOwnerNamespace.Next(), 1024, 64, FrameHeapRoots(plan), 1024);
        return new(plan, heap, arguments);
    }

    private static int LiveFrameCapability(WarpCompiledSourcePlan plan, uint[] state)
    {
        for (int depth = 0; depth < state[WarpLogicalMachineLayout.DepthOffset]; depth++)
        {
            int frame = checked(WarpLogicalMachineLayout.HeaderWords + depth * plan.Layout.FrameWords);
            int function = checked((int)state[frame + WarpLogicalMachineLayout.FrameFunctionOffset]);
            WarpPortableWordBody? body = plan.Program.Bodies.FirstOrDefault(item => item.Function == function);
            if (body is null) { continue; }
            WarpLogicalMachineNode node = plan.Layout.Nodes[checked((int)state[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset])];
            WarpPortableWordSourceBlock? block = body.SourceBlocks.FirstOrDefault(item => item.GeneratedBlocks.Contains(node.Block));
            if (block is null) { continue; }
            foreach (WarpPortableWordRoot root in block.Roots.Where(item => item.Source.IsInteriorOwner))
            {
                int address = checked(frame + plan.Layout.PrivateOffset + root.PrivateWordOffset);
                if (state.AsSpan(address, 6).IndexOfAnyExcept(0u) >= 0) { return address; }
            }
        }
        return -1;
    }
}
