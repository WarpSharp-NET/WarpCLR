using System.Diagnostics.CodeAnalysis;
using System.Reflection.Emit;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Verifier;

namespace WarpCLR.Tests;

[TestClass]
[DoNotParallelize]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this fixture.")]
internal sealed class WarpSourceUnsupportedEndTests
{
    [TestMethod]
    public void UnsupportedSchedulerWaitCannotInventAStatusBasedSourceEnd()
    {
        Assert.ThrowsExactly<WarpVerificationException>(() => WarpSourceEndChildFixture.Capture(typeof(WarpSourceEndKernels), nameof(WarpSourceEndKernels.Wait)));
        WarpSourceEventFixture fixture = WarpSourceEventFixture.Capture();
        WarpSourceSegmentSnapshot origin = fixture.CandidateOrigin(); WarpPortableSourceSegment segment = fixture.Map.Segments[0];
        Assert.IsFalse(Enum.GetNames<WarpPortableSourceSegmentEndKind>().Any(name => name.Contains("Wait", StringComparison.Ordinal)));
        foreach (uint status in new uint[] { WarpLogicalMachineLayout.Completed, WarpLogicalMachineLayout.Faulted, 5, 7, 8 })
        {
            WarpSourceSegmentSnapshot allegedWait = WarpSourceEventFixture.Copy(origin, state =>
            { state[WarpLogicalMachineLayout.StatusOffset] = status; state[WarpLogicalMachineLayout.RemainingStepsLowOffset]--; });
            Assert.ThrowsExactly<InvalidOperationException>(() => WarpSourceSegmentEndContract.ValidateCandidate(fixture.Map, segment,
                allegedWait, WarpSourceEventFixture.MaximumDepth, 2, (WarpPortableSourceSegmentEndKind)5, 1000));
        }
    }

    [TestMethod]
    public async Task ActualEmptyCallChildExhaustsBudgetAtChargedRetAndCannotBecomeAWaitOrSuccessfulEnd()
    {
        WarpSourceEndChildFixture fixture = WarpSourceEndChildFixture.Capture(typeof(WarpSourceEndKernels), nameof(WarpSourceEndKernels.EmptyCall));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => fixture.Map.Layout.CreateInitialState(WarpSourceEventFixture.MaximumDepth, 0));
        WarpCoreCLRWorkerKernel kernel = await fixture.CompileAsync().ConfigureAwait(false);
        await using var kernelOwner = kernel.ConfigureAwait(false); WarpCoreCLRWorkerLease lease = kernel.TryAcquireLease()!;
        await using var leaseOwner = lease.ConfigureAwait(false);
        foreach (int quantum in new[] { fixture.Map.Layout.MaximumBlockCost, Math.Max(4096, fixture.Map.Layout.MaximumBlockCost) })
        {
            uint[] state = fixture.Map.Layout.CreateInitialState(WarpSourceEventFixture.MaximumDepth, 1); uint[] arena = [0xDEADBEEF];
            fixture.Map.Layout.SetSourceBoundaryMode(state, true);
            WarpSourceSegmentSnapshot start = fixture.Snapshot(lease, 0, state, arena);
            ulong ordinal = await fixture.RunToFirstGuestAsync(lease, [[0xFEDCBA98]], state, arena, quantum).ConfigureAwait(false);
            ordinal = await SpendLastStepAsync(lease, state, arena, quantum, ordinal).ConfigureAwait(false);
            WarpSourceSegmentSnapshot parked = fixture.Snapshot(lease, ordinal, state, arena);
            int frame = WarpLogicalMachineLayout.HeaderWords + ((int)state[WarpLogicalMachineLayout.DepthOffset] - 1) * fixture.Map.Layout.FrameWords;
            WarpLogicalMachineNode node = fixture.Map.Layout.Nodes[(int)state[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset]];
            WarpPortableSourceSegment segment = fixture.Map.Find(node.Function, node.Block); Assert.AreEqual(OpCodes.Ret.Value, segment.SourceOpcode);
            WarpLogicalMachineLayout.AcknowledgeSourceBoundary(state);
            await lease.ExecuteManagedQuantumAsync([[0xFEDCBA98]], [], 0, state, WarpSourceEventFixture.MaximumDepth, quantum, arena, CancellationToken.None).ConfigureAwait(false);
            WarpSourceSegmentSnapshot failed = fixture.Snapshot(lease, ordinal + 1, state, arena);
            fixture.WriteWitness("BudgetZero", lease, quantum, start, parked, failed);
            Assert.AreEqual(WarpLogicalMachineLayout.Faulted, state[0]); Assert.AreEqual(1u, state[1]);
            Assert.ThrowsExactly<InvalidOperationException>(() => WarpSourceSegmentEndContract.ValidateCandidate(fixture.Map, segment, failed,
                WarpSourceEventFixture.MaximumDepth, (int)state[WarpLogicalMachineLayout.DepthOffset], WarpPortableSourceSegmentEndKind.SourceReturn, 0));
            Assert.ThrowsExactly<InvalidOperationException>(() => WarpSourceInvocationEndContract.ValidateCandidate(fixture.Map, fixture.Map.Invocation,
                start, failed, WarpSourceEventFixture.MaximumDepth, [0xFEDCBA98], failed.StateHash, failed.ArenaHash));
        }
    }

    private static async Task<ulong> SpendLastStepAsync(WarpCoreCLRWorkerLease lease, uint[] state, uint[] arena, int quantum, ulong ordinal)
    {
        WarpLogicalMachineLayout.AcknowledgeSourceBoundary(state);
        do
        {
            await lease.ExecuteManagedQuantumAsync([[0xFEDCBA98]], [], 0, state, WarpSourceEventFixture.MaximumDepth, quantum, arena, CancellationToken.None).ConfigureAwait(false);
            ordinal++; Assert.AreEqual(0ul, WarpSourceSegmentCheckpointContract.Remaining(state));
        }
        while (state[0] == WarpLogicalMachineLayout.Runnable &&
            state[WarpLogicalMachineLayout.SourceBoundaryStateOffset] != WarpLogicalMachineLayout.BeforeSourceBoundary && ordinal < 5000);
        Assert.AreEqual(WarpLogicalMachineLayout.BeforeSourceBoundary, state[WarpLogicalMachineLayout.SourceBoundaryStateOffset]);
        Assert.IsLessThan(5000ul, ordinal); return ordinal;
    }
}
