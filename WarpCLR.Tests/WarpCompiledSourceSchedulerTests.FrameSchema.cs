using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpCompiledSourceSchedulerTests
{
    [TestMethod]
    public void RequiredSourceFrameMetadataRejectsMissingAndChangedViewsBeforeSchedulerAttachment()
    {
        WarpCompiledSourcePlan plan = CreateFramedLoopPlan(5, 1);
        Assert.IsNotNull(plan.FrameSchema);
        Assert.IsFalse(plan.HasLocalSourceKernel);
        uint roots = FrameHeapRoots(plan);
        uint[][] arguments = [Enumerable.Repeat(4u, 5).ToArray()];
        uint[] missing = plan.TypeSchema.CreateArena(WarpLogicalOwnerNamespace.Next(), 1024, 64, roots, plan.Workers, 1024);
        uint[] beforeMissing = (uint[])missing.Clone();
        Assert.ThrowsExactly<ArgumentException>(() => new WarpCompiledSourceContext(plan, missing, arguments));
        CollectionAssert.AreEqual(beforeMissing, missing);

        uint[] canonical = plan.CreateUnusedHeap(WarpLogicalOwnerNamespace.Next(), 1024, 64, roots, 1024);
        uint descriptor = canonical[WarpPortableSourceMemoryLayout.Descriptor];
        Assert.AreEqual((uint)plan.FrameSchema.Bodies.Length, canonical[descriptor + WarpPortableSourceMemoryLayout.FrameCount]);
        foreach (uint offset in new[]
        {
            descriptor + WarpPortableSourceMemoryLayout.FrameCount,
            descriptor + WarpPortableSourceMemoryLayout.FrameHash,
            descriptor + WarpPortableSourceMemoryLayout.FrameStart,
            descriptor + WarpPortableSourceMemoryLayout.FrameViewCount,
        })
        {
            uint[] changed = (uint[])canonical.Clone();
            changed[offset] ^= 1;
            uint[] beforeChanged = (uint[])changed.Clone();
            Assert.ThrowsExactly<ArgumentException>(() => new WarpCompiledSourceContext(plan, changed, arguments));
            CollectionAssert.AreEqual(beforeChanged, changed);
        }
        Assert.IsFalse(plan.HasLocalSourceKernel);
    }

    [TestMethod]
    public void CanonicalCompiledFrameSchemaRunsEveryOversubscribedOriginalLoop()
    {
        WarpCompiledSourcePlan plan = CreateFramedLoopPlan(13, 2);
        Assert.IsNotNull(plan.FrameSchema);
        WarpCompiledSourceEvidence.Capture(plan);
        uint[] counts = Enumerable.Range(1, 13).Select(value => (uint)value).ToArray();
        uint[] heap = plan.CreateUnusedHeap(WarpLogicalOwnerNamespace.Next(), 1024, 64, FrameHeapRoots(plan), 1024);
        var context = new WarpCompiledSourceContext(plan, heap, [counts]);
        Assert.IsFalse(plan.HasLocalSourceKernel);
        Drain(context);
        Assert.IsTrue(plan.HasLocalSourceKernel);
        Assert.AreEqual(WarpPortableSchedulerLayout.CompletedContext, context.State);
        for (uint worker = 0; worker < 13; worker++)
        {
            Assert.AreEqual(counts[worker] * (counts[worker] + 1) / 2, context.Result(worker, context.Dispatch)[0]);
            Assert.IsGreaterThan(0u, context.WorkerQuanta(worker));
            Assert.AreEqual(0u, context.Arena[context.Scheduler + WarpPortableSchedulerLayout.ControllerOwner]);
        }
    }

    private static WarpCompiledSourcePlan CreateFramedLoopPlan(uint workers, uint residents)
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Kernels).GetMethod(nameof(Kernels.Loop))!);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        WarpPortableClosedFrameSourcePlan frames = WarpPortableClosedFrameSourcePlan.Capture(graph, typed, schema);
        WarpPortableWordLoweredProgram program = WarpPortableWordLowerer.Lower(graph, typed, schema,
            new WarpPortableClosedFrameSourceBinding(frames));
        return new(graph, schema, program, workers, residents, 16, 100000, 1, Services.Value);
    }

    private static uint FrameHeapRoots(WarpCompiledSourcePlan plan) =>
        checked(plan.Workers * ((uint)plan.RootTupleCount + 1 + (uint)plan.ResultRootWords.Length) + 4);
}
