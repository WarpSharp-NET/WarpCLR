using System.Diagnostics.CodeAnalysis;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

[TestClass]
[DoNotParallelize]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this fixture.")]
internal sealed class WarpSourceEventChildTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(nameof(WarpSourceEventKernels.Invoke))]
    [DataRow(nameof(WarpSourceEventKernels.Indirect))]
    [DataRow(nameof(WarpSourceEventKernels.Packed))]
    public async Task ActualChildQuantaMatchFiniteSegmentsWithoutIssuingSourceAuthority(string method)
    {
        WarpSourceEventFixture fixture = WarpSourceEventFixture.Capture(method);
        string refs = Environment.GetEnvironmentVariable("WARP_SOURCE_EVENTS_REFS") ?? throw new InvalidOperationException("The immutable reference directory is required.");
        var options = new WarpCoreCLRWorkerOptions { DotnetHostPath = "/home/codex/.dotnet/dotnet", WorkerAssemblyPath = Path.Combine(refs, "WarpCLR.CoreCLR.Worker", "release", "WarpCLR.CoreCLR.Worker.dll") };
        WarpCoreCLRWorkerKernel kernel = await WarpCoreCLRWorkerKernel.CompileAsync(fixture.Map.Layout, options, CancellationToken.None).ConfigureAwait(false);
        await using var owner = kernel.ConfigureAwait(false);
        WarpCoreCLRWorkerLease lease = kernel.TryAcquireLease()!;
        await using var leaseOwner = lease.ConfigureAwait(false);
        Assert.AreNotEqual(Environment.ProcessId, lease.ProcessId);
        foreach (int quantum in new[] { fixture.Map.Layout.MaximumBlockCost, 4096 })
        {
            ChildCounters counters = await RunAsync(fixture, lease, quantum).ConfigureAwait(false);
            TestContext.WriteLine($"{method}: quantum={quantum}; pid={lease.ProcessId}; module={lease.CompiledModule}; original-segments={counters.Segments}; child-rpcs={counters.Rpcs}; ends={counters.Ends}; candidate-data-only");
            Assert.AreEqual(counters.Segments, counters.Ends); Assert.IsGreaterThan(0, counters.Segments);
        }
    }

    private static async Task<ChildCounters> RunAsync(WarpSourceEventFixture fixture, WarpCoreCLRWorkerLease lease, int quantum)
    {
        WarpLogicalMachineLayout layout = fixture.Map.Layout; uint[][] inputs = [[0x81234567]];
        uint[] state = layout.CreateInitialState(WarpSourceEventFixture.MaximumDepth, 1000000);
        uint[] arena = [0xAABBCCDD, 0x80000000, 0x7FC12345, uint.MaxValue]; uint[] canaries = (uint[])arena.Clone();
        layout.SetSourceBoundaryMode(state, true);
        ulong ordinal = 0; int segments = 0, ends = 0;
        await lease.ExecuteManagedQuantumAsync(inputs, [], 0, state, WarpSourceEventFixture.MaximumDepth, quantum, arena, CancellationToken.None).ConfigureAwait(false); ordinal++;
        while (state[0] == WarpLogicalMachineLayout.Runnable)
        {
            Assert.AreEqual(WarpLogicalMachineLayout.BeforeSourceBoundary, state[WarpLogicalMachineLayout.SourceBoundaryStateOffset]);
            int depth = checked((int)state[WarpLogicalMachineLayout.DepthOffset]); int frame = WarpLogicalMachineLayout.HeaderWords + (depth - 1) * layout.FrameWords;
            WarpLogicalMachineNode node = layout.Nodes[(int)state[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset]];
            WarpPortableSourceSegment segment = fixture.Map.Find(node.Function, node.Block);
            WarpSourceSegmentSnapshot origin = Snapshot(lease, fixture, ordinal, state, arena); WarpSourceSegmentSnapshot previous = origin;
            var contract = new WarpSourceSegmentCheckpointContract(fixture.Map, segment, origin, WarpSourceEventFixture.MaximumDepth, depth);
            WarpLogicalMachineLayout.AcknowledgeSourceBoundary(state); segments++;
            do
            {
                Assert.IsLessThan(25000ul, ordinal);
                await lease.ExecuteManagedQuantumAsync(inputs, [], 0, state, WarpSourceEventFixture.MaximumDepth, quantum, arena, CancellationToken.None).ConfigureAwait(false); ordinal++;
                WarpSourceSegmentSnapshot current = Snapshot(lease, fixture, ordinal, state, arena);
                contract.ValidateExactCommittedCandidate(current, previous, ordinal, ordinal, current.StateHash, current.ArenaHash); previous = current;
            }
            while (state[0] == WarpLogicalMachineLayout.Runnable && state[WarpLogicalMachineLayout.SourceBoundaryStateOffset] != WarpLogicalMachineLayout.BeforeSourceBoundary);
            ValidateEnd(fixture, segment, previous, origin); ends++;
        }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[0]); CollectionAssert.AreEqual(canaries, arena);
        string entry = fixture.Graph.Methods.First(method => string.Equals(method.Identity, fixture.Graph.EntryIdentity, StringComparison.Ordinal)).SourceMethod.Name;
        ulong expected = MethodResult(entry);
        Assert.AreEqual((uint)expected, state[WarpLogicalMachineLayout.ResultOffset]);
        if (layout.ResultWordCount == 2) { Assert.AreEqual((uint)(expected >> 32), state[WarpLogicalMachineLayout.ResultHighOffset]); }
        return new(segments, checked((int)ordinal), ends);
    }

    private static ulong MethodResult(string method) => method switch
    {
        nameof(WarpSourceEventKernels.Invoke) => (0x81234567u ^ 0xA5A5A5A5u) + 1,
        nameof(WarpSourceEventKernels.Indirect) => 0x81234567u + 17,
        nameof(WarpSourceEventKernels.Packed) => 0x81234567ul + 0xA5ul + 0xBEEFul + 0xDEADBEEFul,
        _ => throw new InvalidOperationException("An independent integer oracle is required."),
    };

    private static WarpSourceSegmentSnapshot Snapshot(WarpCoreCLRWorkerLease lease, WarpSourceEventFixture fixture, ulong ordinal, uint[] state, uint[] arena) =>
        new(ordinal, ordinal, lease.ProcessId, lease.CompiledModule, fixture.Map.IrHash, state, arena);

    private static void ValidateEnd(WarpSourceEventFixture fixture, WarpPortableSourceSegment segment, WarpSourceSegmentSnapshot end, WarpSourceSegmentSnapshot origin)
    {
        // Sequence numbers here count test transactions. They are not opaque
        // private registry receipts and cannot release any controller.
        int depth = checked((int)end.State[WarpLogicalMachineLayout.DepthOffset]);
        int frame = WarpLogicalMachineLayout.HeaderWords + (depth - 1) * fixture.Map.Layout.FrameWords;
        WarpPortableSourceSegmentEndKind kind = end.State[0] == WarpLogicalMachineLayout.Completed ? WarpPortableSourceSegmentEndKind.SourceReturn :
            segment.Frontiers.First(frontier => frontier.ProgramCounter == end.State[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset] &&
                frontier.Function == end.State[frame + WarpLogicalMachineLayout.FrameFunctionOffset]).Kind;
        Assert.AreEqual(WarpSourceSegmentCandidateResult.OpaqueRegistryEndAndPublicationReceiptsRequired,
            WarpSourceSegmentEndContract.ValidateCandidate(fixture.Map, segment, end, WarpSourceEventFixture.MaximumDepth, depth, kind,
                WarpSourceSegmentCheckpointContract.Remaining(origin.State.AsSpan())));
    }

    private sealed record ChildCounters(int Segments, int Rpcs, int Ends);
}
