using WarpCLR.Compiler;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableExceptionSourceDriver
{
    internal void Execute() => ExecuteUntil(() => State[WarpLogicalMachineLayout.StatusOffset] != WarpLogicalMachineLayout.Runnable);

    internal void ExecuteUntil(Func<bool> completed)
    {
        for (int iteration = 0; iteration < 1000000; iteration++)
        {
            if (completed()) { return; }
            Assert.AreEqual(WarpLogicalMachineLayout.Runnable, State[WarpLogicalMachineLayout.StatusOffset], Diagnostic());
            if (State[WarpLogicalMachineLayout.SourceBoundaryStateOffset] == WarpLogicalMachineLayout.BeforeSourceBoundary)
            {
                PublishBoundary();
                WarpLogicalMachineLayout.AcknowledgeSourceBoundary(State);
                Boundaries++;
            }
            compiled.ExecuteManagedQuantum(inputs, [], checked((int)LogicalWorker), State, 256, Math.Max(quantum, Program.Layout.MaximumBlockCost), Arena, CancellationToken.None);
            Quanta++;
            ObserveCapturedCensus();
        }
        Assert.Fail("Generated source/EH execution exceeded its explicit finite quantum limit.");
    }

    internal uint MaximumCapturedFrames { get; private set; }

    private void ObserveCapturedCensus()
    {
        uint start = Descriptor + Arena[Descriptor + WarpPortableExceptionLayout.RecordStart];
        uint count = Arena[Descriptor + WarpPortableExceptionLayout.RecordsPerWorker];
        for (uint index = 0; index < count; index++)
        {
            uint record = start + (LogicalWorker * count + index) * WarpPortableExceptionLayout.RecordWords;
            MaximumCapturedFrames = Math.Max(MaximumCapturedFrames, Arena[record + WarpPortableExceptionLayout.FrameCount]);
        }
    }

    private void PublishBoundary()
    {
        uint scheduled = ScheduledWorker;
        uint stack = Arena[scheduled + WarpPortableSchedulerLayout.LogicalStackBase];
        Arena.AsSpan((int)stack, RootCapacity * 3).Clear();
        var references = new List<uint[]>();
        for (uint physical = 0; physical < State[WarpLogicalMachineLayout.DepthOffset]; physical++)
        {
            int frame = WarpLogicalMachineLayout.HeaderWords + checked((int)physical) * Program.Layout.FrameWords;
            int function = checked((int)State[frame]); int pc = checked((int)State[frame + 1]);
            if (function == 0)
            {
                foreach (WarpPortableWordTransientRoot root in Program.Lowered.EntryProjection.WrapperInputRoots)
                {
                    Assert.IsFalse(root.IsInteriorOwner);
                    references.Add(State.AsSpan(frame + WarpLogicalMachineLayout.FrameHeaderWords + root.SsaWordOffset, 3).ToArray());
                }
            }
            else if (!Program.Layout.IsRuntimeHelper(function)) { ProjectSourceRoots(references, frame, function, pc); }
        }
        Assert.IsLessThanOrEqualTo(RootCapacity, references.Count);
        for (int index = 0; index < references.Count; index++) { references[index].CopyTo(Arena, checked((int)stack + index * 3)); }
        int top = WarpLogicalMachineLayout.HeaderWords + checked((int)State[WarpLogicalMachineLayout.DepthOffset] - 1) * Program.Layout.FrameWords;
        int topFunction = checked((int)State[top]); int topPc = checked((int)State[top + 1]);
        uint revision = Arena[scheduled + WarpPortableSchedulerLayout.RootRevision] + 1;
        Assert.AreEqual(0u, Scheduled(nameof(WarpPortableSchedulerServices.PublishRoots), LogicalWorker, RunGeneration,
            Arena[Scheduler + WarpPortableSchedulerLayout.GCEpoch], maps[(topFunction, topPc)], revision, (uint)topFunction, (uint)topPc));
        if (inputRoots.Count != 0)
        {
            foreach ((uint root, uint generation) in inputRoots) { Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.ReleaseRoot), root, generation)); }
            inputRoots.Clear();
        }
    }

    private void ProjectSourceRoots(List<uint[]> references, int frame, int function, int pc)
    {
        WarpLogicalMachineNode node = Program.Layout.Nodes[pc];
        WarpPortableWordBody body = Program.Lowered.Bodies.First(body => body.Function == function);
        WarpPortableWordSourceBlock block = body.SourceBlocks.First(block => block.GeneratedBlocks.Contains(node.Block));
        foreach (WarpPortableWordRoot root in block.Roots)
        {
            Assert.IsFalse(root.Source.IsInteriorOwner);
            int actualFrame = frame;
            if (body.AliasOwnerFunction != -1 && root.PrivateWordOffset < body.AliasPrefixWords)
            {
                uint ownerPhysical = State[frame + WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset];
                actualFrame = WarpLogicalMachineLayout.HeaderWords + checked((int)(ownerPhysical - 1)) * Program.Layout.FrameWords;
                Assert.AreEqual((uint)body.AliasOwnerFunction, State[actualFrame]);
                Assert.AreEqual(State[frame + WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset], State[actualFrame + WarpLogicalMachineLayout.FrameActivationOffset]);
            }
            uint[] owner = State.AsSpan(actualFrame + Program.Layout.PrivateOffset + root.PrivateWordOffset, 3).ToArray();
            if (owner[1] != 0)
            {
                Assert.AreEqual(Arena[WarpPortableHeapLayout.Context], owner[0]);
                uint slot = Arena[WarpPortableHeapLayout.SlotStart] + (owner[1] - 1) * WarpPortableHeapLayout.SlotWords;
                Assert.AreEqual(WarpPortableHeapLayout.Allocated, Arena[slot + WarpPortableHeapLayout.SlotState]);
                Assert.AreEqual(owner[2], Arena[slot + WarpPortableHeapLayout.SlotGeneration]);
            }
            references.Add(owner);
        }
    }
}
