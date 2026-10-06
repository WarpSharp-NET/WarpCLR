using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests;

internal sealed partial class WarpSourceGuardedChildDriver
{
    private Task<uint> HeapAsync(string name, params uint[] args) => ServiceAsync(typeof(WarpPortableHeapServices), name, args);
    private Task<uint> ScheduledAsync(string name, params uint[] args) => ServiceAsync(typeof(WarpPortableSchedulerServices), name, [Scheduler, 1, .. args]);
    private Task<uint> ExceptionAsync(string name, params uint[] args) => ServiceAsync(typeof(WarpPortableExceptionServices), name, [1, 0, RunGeneration, DispatchGeneration, .. args]);

    private async Task InitializeAsync()
    {
        Owner = await AllocateAsync(Fixture.Schema.TypeId(WarpPortableMethodGraphIdentity.Type(typeof(InvalidOperationException))), false).ConfigureAwait(false);
        Assert.AreEqual(0u, await HeapAsync(nameof(WarpPortableHeapServices.AcquireRoot), Owner[0], Owner[1], Owner[2], WarpPortableHeapLayout.StrongRoot, 0, 0, 0).ConfigureAwait(false));
        var traces = new List<uint[]>();
        for (int index = 0; index < 16; index++) { traces.Add(await AllocateAsync(Plan.TraceType, true).ConfigureAwait(false)); }
        // This value is the old concrete EH fixture ABI, never an opaque grant.
        Arena[Scheduler + WarpPortableSchedulerLayout.ControllerOwner] = 1;
        Assert.AreEqual(0u, await ScheduledAsync(nameof(WarpPortableSchedulerServices.TryAcquireWorker), 0).ConfigureAwait(false));
        Assert.AreEqual(0u, await ScheduledAsync(nameof(WarpPortableSchedulerServices.AcquireHeapService), 0, RunGeneration).ConfigureAwait(false));
        for (int index = 0; index < traces.Count; index++)
        {
            uint[] trace = traces[index];
            Assert.AreEqual(0u, await ExceptionAsync(nameof(WarpPortableExceptionServices.BindTraceReport), (uint)index + 1, trace[0], trace[1], trace[2]).ConfigureAwait(false));
        }
        Assert.AreEqual(0u, await ExceptionAsync(nameof(WarpPortableExceptionServices.SealReports)).ConfigureAwait(false));
    }

    private async Task<uint[]> AllocateAsync(uint type, bool array)
    {
        Assert.AreEqual(0u, await HeapAsync(nameof(WarpPortableHeapServices.AcquireServiceLease), uint.MaxValue).ConfigureAwait(false));
        uint[] args = array ? [type, WarpPortableExceptionTraceLayout.HeaderWords + 16 * WarpPortableExceptionTraceLayout.FrameWords * 2] : [type];
        Assert.AreEqual(0u, await HeapAsync(array ? nameof(WarpPortableHeapServices.AllocateArray) : nameof(WarpPortableHeapServices.AllocateObject), args).ConfigureAwait(false));
        uint[] owner = Arena.AsSpan((int)WarpPortableHeapLayout.Result, 3).ToArray();
        Assert.AreEqual(0u, await HeapAsync(nameof(WarpPortableHeapServices.AcknowledgeServiceResult), uint.MaxValue).ConfigureAwait(false));
        Assert.AreEqual(0u, await HeapAsync(nameof(WarpPortableHeapServices.ReleaseServiceLease), uint.MaxValue).ConfigureAwait(false));
        return owner;
    }

    internal async Task ExecuteAsync(int quantum)
    {
        if (State[WarpLogicalMachineLayout.SourceBoundaryStateOffset] == WarpLogicalMachineLayout.BeforeSourceBoundary)
        { await PublishAsync().ConfigureAwait(false); WarpLogicalMachineLayout.AcknowledgeSourceBoundary(State); }
        uint[][] inputs = Owner.Concat(Fixture.Map.Layout.Kernel.InputBufferCount == 4 ? [flag] : Array.Empty<uint>()).Select(word => new[] { word }).ToArray();
        await Lease.ExecuteManagedQuantumAsync(inputs, [], 0, State, WarpSourceEventFixture.MaximumDepth,
            Math.Max(quantum, Fixture.Map.Layout.MaximumBlockCost), Arena, CancellationToken.None).ConfigureAwait(false); Ordinal++;
    }

    private async Task PublishAsync()
    {
        uint worker = Scheduler + Arena[Scheduler + WarpPortableSchedulerLayout.WorkerStart]; uint stack = Arena[worker + WarpPortableSchedulerLayout.LogicalStackBase];
        var references = new List<uint[]>();
        for (int physical = 0; physical < State[WarpLogicalMachineLayout.DepthOffset]; physical++)
        {
            int frame = WarpLogicalMachineLayout.HeaderWords + physical * Fixture.Map.Layout.FrameWords; int function = (int)State[frame];
            if (function == 0 || Fixture.Map.Layout.IsRuntimeHelper(function)) { continue; }
            int pc = (int)State[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset]; WarpLogicalMachineNode node = Fixture.Map.Layout.Nodes[pc];
            WarpPortableWordBody body = Fixture.Program.Bodies.First(body => body.Function == function);
            WarpPortableWordSourceBlock block = body.SourceBlocks.First(block => block.GeneratedBlocks.Contains(node.Block));
            foreach (WarpPortableWordRoot root in block.Roots)
            {
                int owner = body.AliasOwnerFunction != -1 && root.PrivateWordOffset < body.AliasPrefixWords ?
                    WarpLogicalMachineLayout.HeaderWords + ((int)State[frame + WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset] - 1) * Fixture.Map.Layout.FrameWords : frame;
                Assert.IsFalse(root.Source.IsInteriorOwner); uint[] value = State.AsSpan(owner + Fixture.Map.Layout.PrivateOffset + root.PrivateWordOffset, 3).ToArray();
                Assert.IsTrue(value.All(word => word == 0) || value.SequenceEqual(Owner)); references.Add(value);
            }
        }
        Assert.IsLessThanOrEqualTo(RootCapacity, references.Count); Arena.AsSpan((int)stack, RootCapacity * 3).Clear();
        for (int index = 0; index < references.Count; index++) { references[index].CopyTo(Arena, checked((int)stack + index * 3)); }
        int top = WarpLogicalMachineLayout.HeaderWords + ((int)State[WarpLogicalMachineLayout.DepthOffset] - 1) * Fixture.Map.Layout.FrameWords;
        int topFunction = (int)State[top], topPc = (int)State[top + WarpLogicalMachineLayout.FrameProgramCounterOffset];
        Assert.AreEqual(0u, await ScheduledAsync(nameof(WarpPortableSchedulerServices.PublishRoots), 0, RunGeneration,
            Arena[Scheduler + WarpPortableSchedulerLayout.GCEpoch], maps[(topFunction, topPc)], checked(Arena[worker + WarpPortableSchedulerLayout.RootRevision] + 1), (uint)topFunction, (uint)topPc).ConfigureAwait(false));
    }
}
