using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableClosedFrameExceptionDriver
{
    internal void Execute() => ExecuteUntil(() => State[WarpLogicalMachineLayout.StatusOffset] != WarpLogicalMachineLayout.Runnable);

    internal void ExecuteUntil(Func<bool> completed)
    {
        for (int iteration = 0; iteration < 1000000; iteration++)
        {
            if (completed()) { return; }
            Assert.AreEqual(WarpLogicalMachineLayout.Runnable, State[WarpLogicalMachineLayout.StatusOffset]);
            if (State[WarpLogicalMachineLayout.SourceBoundaryStateOffset] == WarpLogicalMachineLayout.BeforeSourceBoundary)
            {
                PublishBoundary();
                WarpLogicalMachineLayout.AcknowledgeSourceBoundary(State);
                Boundaries++;
            }
            compiled.ExecuteManagedQuantum(inputs, [], 0, State, MaximumDepth,
                Math.Max(quantum, Program.Layout.MaximumBlockCost), Arena, CancellationToken.None);
            Quanta++;
        }
        Assert.Fail("Actual captured value-constructor/EH execution exceeded its bounded proof quanta.");
    }

    internal void PublishBoundary()
    {
        uint scheduled = ScheduledWorker;
        uint stack = Arena[scheduled + WarpPortableSchedulerLayout.LogicalStackBase];
        var references = new List<uint[]>();
        for (uint physical = 0; physical < State[WarpLogicalMachineLayout.DepthOffset]; physical++)
        {
            int frame = WarpLogicalMachineLayout.HeaderWords + checked((int)physical) * Program.Layout.FrameWords;
            int function = checked((int)State[frame]);
            int pc = checked((int)State[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset]);
            if (function == 0)
            {
                foreach (WarpPortableWordTransientRoot root in Program.Lowered.EntryProjection.WrapperInputRoots)
                {
                    Assert.IsFalse(root.IsInteriorOwner);
                    AddReference(references, State.AsSpan(frame + WarpLogicalMachineLayout.FrameHeaderWords + root.SsaWordOffset, 3).ToArray());
                }
            }
            else if (!Program.Layout.IsRuntimeHelper(function)) { ProjectSourceRoots(references, frame, function, pc); }
        }
        Assert.IsLessThanOrEqualTo(RootCapacity, references.Count);
        // Validate the entire census before replacing the previously published
        // root bank. A denied owner never clears roots already held by the lease.
        Arena.AsSpan((int)stack, RootCapacity * 3).Clear();
        for (int index = 0; index < references.Count; index++) { references[index].CopyTo(Arena, checked((int)stack + index * 3)); }
        int top = WarpLogicalMachineLayout.HeaderWords + checked((int)State[WarpLogicalMachineLayout.DepthOffset] - 1) * Program.Layout.FrameWords;
        int topFunction = checked((int)State[top]);
        int topPc = checked((int)State[top + WarpLogicalMachineLayout.FrameProgramCounterOffset]);
        uint revision = checked(Arena[scheduled + WarpPortableSchedulerLayout.RootRevision] + 1);
        Assert.AreEqual(0u, Scheduled(nameof(WarpPortableSchedulerServices.PublishRoots), 0, RunGeneration,
            Arena[Scheduler + WarpPortableSchedulerLayout.GCEpoch], maps[(topFunction, topPc)], revision, (uint)topFunction, (uint)topPc));
        foreach ((uint root, uint generation) in inputRoots)
        {
            Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.ReleaseRoot), root, generation));
        }
        inputRoots.Clear();
    }

    private void ProjectSourceRoots(List<uint[]> references, int frame, int function, int pc)
    {
        WarpLogicalMachineNode node = Program.Layout.Nodes[pc];
        WarpPortableWordBody body = Program.Lowered.Bodies.First(body => body.Function == function);
        WarpPortableWordSourceBlock block = body.SourceBlocks.First(block => block.GeneratedBlocks.Contains(node.Block));
        foreach (WarpPortableWordRoot root in block.Roots)
        {
            int actualFrame = frame;
            if (body.AliasOwnerFunction != -1 && root.PrivateWordOffset < body.AliasPrefixWords)
            {
                uint ownerPhysical = State[frame + WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset];
                Assert.IsTrue(ownerPhysical > 0 && ownerPhysical <= State[WarpLogicalMachineLayout.DepthOffset]);
                actualFrame = WarpLogicalMachineLayout.HeaderWords + checked((int)(ownerPhysical - 1)) * Program.Layout.FrameWords;
                Assert.AreEqual((uint)body.AliasOwnerFunction, State[actualFrame]);
                Assert.AreEqual(State[frame + WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset],
                    State[actualFrame + WarpLogicalMachineLayout.FrameActivationOffset]);
            }
            int word = actualFrame + Program.Layout.PrivateOffset + root.PrivateWordOffset;
            if (root.Source.IsInteriorOwner)
            {
                ValidateFrameOwner(State.AsSpan(word, 6).ToArray(), RootElementType(body, block, root));
                references.Add([0, 0, 0]);
            }
            else { AddReference(references, State.AsSpan(word, 3).ToArray()); }
        }
    }

    private uint RootElementType(WarpPortableWordBody body, WarpPortableWordSourceBlock block, WarpPortableWordRoot root)
    {
        string identity = root.Source.Storage switch
        {
            "argument" => body.Arguments[root.Source.Slot].Type.Identity,
            "local" => body.Locals[root.Source.Slot].Type.Identity,
            "stack" => block.Instruction.EntryStack[root.Source.Slot].TypeIdentity,
            _ => throw new InvalidOperationException("An interior root has no immutable argument/local/evaluation type binding."),
        };
        WarpPortableTypedType type = Program.Typed.Types.First(type => string.Equals(type.Identity, identity, StringComparison.Ordinal));
        Assert.AreEqual(WarpPortableStackCategory.ManagedByref, type.Category);
        Assert.IsNotNull(type.ElementType);
        return Program.Schema.TypeId(type.ElementType);
    }

    private void ValidateFrameOwner(uint[] owner, uint expectedElementType)
    {
        if (owner.All(word => word == 0)) { return; }
        Assert.AreEqual(expectedElementType, owner[5]);
        // The paused original worker is the privileged uint[] bank of a separate
        // generated validation helper. No host method validates a source owner.
        Assert.AreEqual(0u, WarpPortableExceptionTestService.Run(typeof(WarpPortableFrameServices),
            nameof(WarpPortableFrameServices.ValidateOwner), State, owner, quantum));
        int frame = WarpLogicalMachineLayout.HeaderWords + checked((int)(owner[1] - 1)) * Program.Layout.FrameWords;
        uint function = State[frame + WarpLogicalMachineLayout.FrameFunctionOffset];
        uint privateWords = State[frame + WarpLogicalMachineLayout.FramePrivateWordsOffset];
        Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.ValidateSourceFrameView), function, privateWords,
            owner[3], owner[4], owner[5]));
        FrameOwnersValidated++;
    }

    private void AddReference(List<uint[]> references, uint[] owner)
    {
        if (owner[1] == 0)
        {
            Assert.AreEqual(0u, owner[0] | owner[2]);
        }
        else
        {
            Assert.AreEqual(0u, Heap(nameof(WarpPortableHeapServices.GetType), owner[0], owner[1], owner[2]));
        }
        references.Add(owner);
    }
}
