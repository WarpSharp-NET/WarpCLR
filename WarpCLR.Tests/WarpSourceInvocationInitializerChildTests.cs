using System.Diagnostics.CodeAnalysis;
using System.Reflection.Emit;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

[TestClass]
[DoNotParallelize]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this fixture.")]
internal sealed class WarpSourceInvocationInitializerChildTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ActualStrictCctorEntryStopsBeforeItsFirstChargeAndExposesTheZeroChargeCompletionSeam()
    {
        using var original = new WarpSourceEndInitializer();
        WarpSourceEndChildFixture fixture = WarpSourceEndChildFixture.Capture(original.Method, initializer: true);
        WarpPortableWordInvocationPrelude prelude = fixture.Map.Invocation.Prelude!; Assert.IsNotNull(prelude);
        Assert.IsTrue(prelude.GeneratedBlocks.All(block => fixture.Program.Kernel.Execution!.Bodies[fixture.Map.Invocation.Function].SourceBlockCosts[block] == 0));
        WarpCoreCLRWorkerKernel kernel = await fixture.CompileAsync().ConfigureAwait(false);
        await using var kernelOwner = kernel.ConfigureAwait(false); WarpCoreCLRWorkerLease lease = kernel.TryAcquireLease()!;
        await using var leaseOwner = lease.ConfigureAwait(false);
        foreach (int quantum in new[] { fixture.Map.Layout.MaximumBlockCost, Math.Max(4096, fixture.Map.Layout.MaximumBlockCost) })
        {
            uint[] arena = fixture.Schema.CreateArena(0x902, 4096, 16, 16, 1, 4096);
            uint[] state = fixture.Map.Layout.CreateInitialState(WarpSourceEventFixture.MaximumDepth, 10000);
            fixture.Map.Layout.SetSourceBoundaryMode(state, true); uint[][] inputs = [[0x76543210], [0xFEDCBA98]];
            WarpSourceSegmentSnapshot start = fixture.Snapshot(lease, 0, state, arena);
            ulong firstOrdinal = await fixture.RunToFirstGuestAsync(lease, inputs, state, arena, quantum).ConfigureAwait(false);
            WarpSourceSegmentSnapshot first = fixture.Snapshot(lease, firstOrdinal, state, arena);
            fixture.WriteWitness("StrictCctor-First", lease, quantum, start, first, first);
            Assert.AreEqual(WarpSourceSegmentCandidateResult.OpaqueRegistryEndAndPublicationReceiptsRequired,
                WarpSourceInvocationEndContract.ValidateCandidate(fixture.Map, fixture.Map.Invocation, start, first,
                    WarpSourceEventFixture.MaximumDepth, [0x76543210, 0xFEDCBA98], first.StateHash, first.ArenaHash));
            Assert.AreEqual(1u, InitializationState(fixture, arena));
            ulong ordinal = await RunToBodyAsync(fixture, lease, inputs, state, arena, quantum, firstOrdinal).ConfigureAwait(false);
            Assert.AreEqual(2u, InitializationState(fixture, arena));
            WarpSourceSegmentSnapshot body = fixture.Snapshot(lease, ordinal, state, arena);
            fixture.WriteWitness("StrictCctor", lease, quantum, start, first, body);
            TestContext.WriteLine($"Strict cctor actual child={lease.ProcessId}, module={lease.CompiledModule}, quantum={quantum}; zero-charge completion crosses after original ret in legacy profile; future private bridge needs separate runtime receipt.");
        }
    }

    private static async Task<ulong> RunToBodyAsync(WarpSourceEndChildFixture fixture, WarpCoreCLRWorkerLease lease,
        uint[][] inputs, uint[] state, uint[] arena, int quantum, ulong ordinal)
    {
        bool sawConstructorReturn = false; ulong beforeReturn = 0;
        for (; ordinal < 5000; ordinal++)
        {
            Assert.AreEqual(WarpLogicalMachineLayout.Runnable, state[0]);
            if (state[WarpLogicalMachineLayout.SourceBoundaryStateOffset] == WarpLogicalMachineLayout.BeforeSourceBoundary)
            {
                int frame = WarpLogicalMachineLayout.HeaderWords + ((int)state[WarpLogicalMachineLayout.DepthOffset] - 1) * fixture.Map.Layout.FrameWords;
                WarpLogicalMachineNode node = fixture.Map.Layout.Nodes[(int)state[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset]];
                if (node.Function == fixture.Map.Invocation.Function)
                {
                    Assert.IsTrue(sawConstructorReturn);
                    Assert.AreEqual(beforeReturn - 1, WarpSourceSegmentCheckpointContract.Remaining(state));
                    return ordinal;
                }
                WarpPortableSourceSegment segment = fixture.Map.Find(node.Function, node.Block);
                if (segment.SourceOpcode == OpCodes.Ret.Value)
                { sawConstructorReturn = true; beforeReturn = WarpSourceSegmentCheckpointContract.Remaining(state); }
                WarpLogicalMachineLayout.AcknowledgeSourceBoundary(state);
            }
            await lease.ExecuteManagedQuantumAsync(inputs, [], 0, state, WarpSourceEventFixture.MaximumDepth, quantum, arena, CancellationToken.None).ConfigureAwait(false);
        }
        throw new InvalidOperationException("The actual cctor did not reach the original entry-body frontier.");
    }

    private static uint InitializationState(WarpSourceEndChildFixture fixture, uint[] arena)
    {
        uint type = fixture.Map.Invocation.Prelude!.DeclaringType;
        int row = checked((int)(arena[WarpPortableHeapLayout.TypeStart] + (type - 1) * WarpPortableHeapLayout.TypeWords));
        return arena[row + WarpPortableHeapLayout.InitializerState];
    }
}
