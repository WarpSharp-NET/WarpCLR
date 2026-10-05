using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpCompiledSourceSchedulerTests
{
    [TestMethod]
    public void ForeignSourceOwnerFailsPreciselyBeforeTheFirstOriginalInstruction()
    {
        WarpCompiledSourceContext context = Create(nameof(Kernels.Identity), 5, 1,
            [Enumerable.Repeat(uint.MaxValue, 5).ToArray(), Enumerable.Repeat(1u, 5).ToArray(), Enumerable.Repeat(1u, 5).ToArray()]);
        context.Advance(0);
        Assert.AreEqual(WarpPortableSchedulerLayout.Quarantined, context.State);
        Assert.AreEqual((uint)context.Plan.MaximumSteps, context.MachineState(0)[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
        WarpCompiledSourceFault fault = context.EscapedFault()!;
        Assert.IsNotNull(fault);
        Assert.AreEqual(4u, fault.Kind);
        Assert.AreEqual(context.Plan.Identity, fault.PlanIdentity, StringComparer.Ordinal);
        Assert.ThrowsExactly<InvalidOperationException>(() => context.Result(0, context.Dispatch));
    }

    [TestMethod]
    public void SourceControllerRejectsChangedRootAndInstructionIdentitiesBeforeExecution()
    {
        var source = CaptureSource(nameof(Kernels.Nested));
        WarpPortableWordLoweredProgram program = source.Program;
        WarpPortableWordBody body = program.Bodies[0];
        WarpPortableWordSourceBlock block = body.SourceBlocks.First(item => !item.Roots.IsEmpty);
        WarpPortableWordBody changed = body with
        {
            SourceBlocks = body.SourceBlocks.Replace(block, block with { Roots = [] }),
        };
        WarpPortableWordLoweredProgram changedRoots = program with { Bodies = program.Bodies.Replace(body, changed) };
        WarpVerificationException roots = Assert.ThrowsExactly<WarpVerificationException>(() =>
            new WarpCompiledSourcePlan(source.Graph, source.Schema, changedRoots, 5, 1, 8, 10000, 1, Services.Value));
        Assert.AreEqual("WRPCLR2350", roots.Code, StringComparer.Ordinal);
        WarpPortableWordLoweredProgram changedInstructions = program with { LoweredHash = "changed" };
        WarpVerificationException instructions = Assert.ThrowsExactly<WarpVerificationException>(() =>
            new WarpCompiledSourcePlan(source.Graph, source.Schema, changedInstructions, 5, 1, 8, 10000, 1, Services.Value));
        Assert.AreEqual("WRPCLR2350", instructions.Code, StringComparer.Ordinal);
    }

    [TestMethod]
    public void RecomputedPublicHashesCannotAdmitManuallyInstrumentedSourcePrograms()
    {
        var source = CaptureSource(nameof(Kernels.OwnerLoop));
        foreach (WarpPortableWordLoweredProgram changed in new[] { DelayOwnerStore(source.Program), DelaySourceEntry(source.Program) })
        {
            WarpVerificationException error = Assert.ThrowsExactly<WarpVerificationException>(() =>
                new WarpCompiledSourcePlan(source.Graph, source.Schema, changed, 7, 1, 4, 10000, 1, Services.Value));
            Assert.AreEqual("WRPCLR2350", error.Code, StringComparer.Ordinal);
        }
    }

    [TestMethod]
    public void ExactSourceSchemaRejectsChangedMemoryViewsAndHeapStateBeforeAttachingAScheduler()
    {
        var source = CaptureSource(nameof(Kernels.Identity));
        var plan = new WarpCompiledSourcePlan(source.Graph, source.Schema, source.Program, 5, 1, 8, 10000, 1, Services.Value);
        uint roots = checked(plan.Workers * ((uint)plan.RootTupleCount + 1 + (uint)plan.ResultRootWords.Length) + 4);
        uint[] original = plan.TypeSchema.CreateArena(WarpLogicalOwnerNamespace.Next(), 1024, 64, roots, plan.Workers, 1024);
        uint descriptor = original[WarpPortableSourceMemoryLayout.Descriptor];
        foreach (uint offset in new[] { WarpPortableHeapLayout.SchemaHash, descriptor + WarpPortableSourceMemoryLayout.Hash,
            descriptor + WarpPortableSourceMemoryLayout.TypeCount, WarpPortableHeapLayout.LeaseState })
        {
            uint[] changed = (uint[])original.Clone();
            changed[offset] ^= 1;
            uint[] before = (uint[])changed.Clone();
            Assert.ThrowsExactly<ArgumentException>(() => new WarpCompiledSourceContext(plan, changed,
                [new uint[5], new uint[5], new uint[5]]));
            CollectionAssert.AreEqual(before, changed);
        }
        var foreign = CaptureSource(nameof(Kernels.Nested));
        uint[] other = foreign.Schema.CreateArena(WarpLogicalOwnerNamespace.Next(), 1024, 64, roots, plan.Workers, 1024);
        Assert.ThrowsExactly<ArgumentException>(() => new WarpCompiledSourceContext(plan, other,
            [new uint[5], new uint[5], new uint[5]]));
        WarpCompiledSourceContext context = Bind(plan, [new uint[5], new uint[5], new uint[5]]);
        uint[] beforeInvalidType = (uint[])context.Arena.Clone();
        Assert.ThrowsExactly<InvalidOperationException>(() => context.QueueEntryAllocation(0, uint.MaxValue));
        CollectionAssert.AreEqual(beforeInvalidType, context.Arena);
    }

    [TestMethod]
    public void AuxiliarySwitchContinuationsKeepTheirOriginalSourceAndPreciseOwnerRoots()
    {
        WarpCompiledSourceContext context = Create(nameof(Kernels.SwitchOwner), 5, 1,
            [new uint[5], new uint[5], new uint[5], [0, 1, 2, 3, 7]]);
        for (uint worker = 0; worker < 5; worker++) { context.QueueEntryAllocation(worker, ObjectType(context)); }
        bool pausedAuxiliary = false;
        for (int attempt = 0; attempt < 10000 && context.State == WarpPortableSchedulerLayout.Active; attempt++)
        {
            context.Advance(0);
            for (uint worker = 0; worker < 5; worker++)
            {
                uint[] state = context.MachineState(worker).ToArray();
                WarpCompiledSourceLocation location = context.Plan.Location(state);
                if (location.Block is null || location.ProgramCounter == context.Plan.Layout.GetBlockEntry(location.Function, location.Block.Block)) { continue; }
                int generated = context.Plan.Layout.Nodes[location.ProgramCounter].Block;
                if (generated == location.Block.Block || !location.Block.GeneratedBlocks.Contains(generated)) { continue; }
                pausedAuxiliary = true;
                Assert.AreEqual(location.Block.Instruction.Offset, checked((int)location.CilOffset));
                uint[] projected = context.Plan.ProjectRoots(state, new uint[4][] { new uint[5], new uint[5], new uint[5], new uint[5] }, worker);
                Assert.IsTrue(Enumerable.Range(0, projected.Length / 3).Any(root =>
                    projected[root * 3] == context.Arena[WarpPortableHeapLayout.Context] && projected[root * 3 + 1] != 0));
            }
        }
        Assert.IsTrue(pausedAuxiliary, "An actual generated auxiliary switch continuation must be observed.");
        Drain(context);
        for (uint worker = 0; worker < 5; worker++)
        {
            Assert.AreEqual(worker < 4 ? context.Arena[WarpPortableHeapLayout.Context] : 0u, context.Result(worker, context.Dispatch)[0]);
        }
    }
}
