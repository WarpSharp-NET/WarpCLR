using System.Diagnostics.CodeAnalysis;
using System.Reflection.Emit;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

[TestClass]
[DoNotParallelize]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this fixture.")]
internal sealed class WarpSourceInvocationChildTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(nameof(WarpSourceEndKernels.Empty))]
    [DataRow(nameof(WarpSourceEndKernels.EmptyCall))]
    [DataRow(nameof(WarpSourceEndKernels.ReturnTriple))]
    public async Task ActualEmptyAndTupleChildrenKeepInvocationSeparateFromChargedOriginalRet(string method)
    {
        WarpSourceEndChildFixture fixture = WarpSourceEndChildFixture.Capture(typeof(WarpSourceEndKernels), method);
        Assert.IsTrue(fixture.Map.Segments.Any(segment => segment.SourceOpcode == OpCodes.Ret.Value));
        Assert.IsTrue(fixture.Map.Segments.Where(segment => segment.SourceOpcode == OpCodes.Ret.Value).All(segment =>
            fixture.Map.Layout.Nodes[segment.EntryProgramCounter].SourceCost == 1));
        AssertInvocation(fixture);
        WarpCoreCLRWorkerKernel kernel = await fixture.CompileAsync().ConfigureAwait(false);
        await using var kernelOwner = kernel.ConfigureAwait(false); WarpCoreCLRWorkerLease lease = kernel.TryAcquireLease()!;
        await using var leaseOwner = lease.ConfigureAwait(false);
        foreach (int quantum in new[] { fixture.Map.Layout.MaximumBlockCost, Math.Max(4096, fixture.Map.Layout.MaximumBlockCost) })
        {
            uint[] arguments = string.Equals(method, nameof(WarpSourceEndKernels.Empty), StringComparison.Ordinal) ? [] : [0xFEDCBA98];
            uint[][] inputs = arguments.Length == 0 ? [[0]] : arguments.Select(word => new[] { word }).ToArray();
            uint[] state = fixture.Map.Layout.CreateInitialState(WarpSourceEventFixture.MaximumDepth, 10000);
            uint[] arena = [0xAABBCCDD, 0x80000000, 0x7FC12345]; fixture.Map.Layout.SetSourceBoundaryMode(state, true);
            WarpSourceSegmentSnapshot start = fixture.Snapshot(lease, 0, state, arena);
            ulong firstOrdinal = await fixture.RunToFirstGuestAsync(lease, inputs, state, arena, quantum).ConfigureAwait(false);
            WarpSourceSegmentSnapshot first = fixture.Snapshot(lease, firstOrdinal, state, arena);
            fixture.WriteWitness("InvocationFirst-" + method, lease, quantum, start, first, first);
            Assert.AreEqual(WarpSourceSegmentCandidateResult.OpaqueRegistryEndAndPublicationReceiptsRequired,
                WarpSourceInvocationEndContract.ValidateCandidate(fixture.Map, fixture.Map.Invocation, start, first,
                    WarpSourceEventFixture.MaximumDepth, arguments, first.StateHash, first.ArenaHash));
            RejectIncompleteInvocation(fixture, start, first, arguments);
            ulong ordinal = await FinishAsync(fixture, lease, inputs, state, arena, quantum, firstOrdinal).ConfigureAwait(false);
            ValidateResult(fixture, method, state);
            fixture.WriteWitness(method, lease, quantum, start, first, fixture.Snapshot(lease, ordinal, state, arena));
            TestContext.WriteLine($"{method}: actual child={lease.ProcessId}, module={lease.CompiledModule}, quantum={quantum}, tuple-width={fixture.Map.Layout.ResultWordCount}; invocation consistency only.");
        }
    }

    private static void AssertInvocation(WarpSourceEndChildFixture fixture)
    {
        WarpPortableSourceInvocation invocation = fixture.Map.Invocation;
        Assert.AreEqual(2, invocation.OriginKind); Assert.IsNull(invocation.SourceOffset); Assert.IsNull(invocation.SourceOpCode);
        Assert.IsNull(invocation.EffectIndex); Assert.IsTrue(invocation.Effects.IsEmpty); Assert.IsTrue(invocation.ExceptionMemberships.IsEmpty);
        Assert.IsTrue(fixture.Map.Segments.All(segment => segment.OriginKind == 1 && segment.CapturedMethodIndex > 0));
        Assert.AreEqual(fixture.Graph.EntryIdentity, fixture.Program.VerifiedProgram.Methods[invocation.CapturedMethodIndex - 1].Identity, StringComparer.Ordinal);
    }

    private static async Task<ulong> FinishAsync(WarpSourceEndChildFixture fixture, WarpCoreCLRWorkerLease lease,
        uint[][] inputs, uint[] state, uint[] arena, int quantum, ulong ordinal)
    {
        while (state[0] == WarpLogicalMachineLayout.Runnable && ordinal < 5000)
        {
            if (state[WarpLogicalMachineLayout.SourceBoundaryStateOffset] == WarpLogicalMachineLayout.BeforeSourceBoundary)
            { WarpLogicalMachineLayout.AcknowledgeSourceBoundary(state); }
            await lease.ExecuteManagedQuantumAsync(inputs, [], 0, state, WarpSourceEventFixture.MaximumDepth, quantum, arena, CancellationToken.None).ConfigureAwait(false); ordinal++;
        }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[0]); Assert.IsLessThan(5000ul, ordinal);
        return ordinal;
    }

    private static void ValidateResult(WarpSourceEndChildFixture fixture, string method, uint[] state)
    {
        int words = fixture.Map.Layout.ResultWordCount;
        if (string.Equals(method, nameof(WarpSourceEndKernels.Empty), StringComparison.Ordinal)) { Assert.AreEqual(0, words); return; }
        uint[] expected = string.Equals(method, nameof(WarpSourceEndKernels.EmptyCall), StringComparison.Ordinal) ? [0xFEDCBA98] : [0xFEDCBA98, 0x89ABCDEF, 0x81234567];
        Assert.AreEqual(expected.Length, words);
        for (int word = 0; word < words; word++)
        { Assert.AreEqual(expected[word], state[fixture.Map.Layout.GetResultWordOffset(word, WarpSourceEventFixture.MaximumDepth)]); }
    }

    private static void RejectIncompleteInvocation(WarpSourceEndChildFixture fixture, WarpSourceSegmentSnapshot start,
        WarpSourceSegmentSnapshot first, uint[] arguments)
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Map.RequireExactInvocation(fixture.Map.Invocation with { }));
        WarpSourceSegmentSnapshot exhausted = WarpSourceEventFixture.Copy(first, state => state[WarpLogicalMachineLayout.RemainingStepsLowOffset]--);
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpSourceInvocationEndContract.ValidateCandidate(fixture.Map,
            fixture.Map.Invocation, start, exhausted, WarpSourceEventFixture.MaximumDepth, arguments, exhausted.StateHash, exhausted.ArenaHash));
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpSourceInvocationEndContract.ValidateCandidate(fixture.Map,
            fixture.Map.Invocation, start, first, WarpSourceEventFixture.MaximumDepth, [1, 2, 3, 4], first.StateHash, first.ArenaHash));
    }
}
