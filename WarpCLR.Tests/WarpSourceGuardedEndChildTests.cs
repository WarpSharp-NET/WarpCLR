using System.Diagnostics.CodeAnalysis;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.Tests;

[TestClass]
[DoNotParallelize]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this fixture.")]
internal sealed class WarpSourceGuardedEndChildTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(nameof(WarpSourceEndKernels.Catch), 1u)]
    [DataRow(nameof(WarpSourceEndKernels.Filter), 1u)]
    [DataRow(nameof(WarpSourceEndKernels.Filter), 0u)]
    [DataRow(nameof(WarpSourceEndKernels.MultipleFilters), 2u)]
    public async Task ActualPreparedNonnullChildrenBindUnwindAliasAndManagedTerminalWithoutEndAuthority(string method, uint flag)
    {
        foreach (bool large in new[] { false, true })
        {
            WarpSourceGuardedChildDriver driver = await WarpSourceGuardedChildDriver.CreateAsync(method, flag).ConfigureAwait(false);
            await using var owner = driver.ConfigureAwait(false); WarpLogicalMachineLayout layout = driver.Fixture.Map.Layout;
            int quantum = large ? Math.Max(4096, layout.MaximumBlockCost) : layout.MaximumBlockCost;
            HashSet<WarpSourceSegmentGuardedDisposition> guarded = await ExecuteAsync(driver, quantum).ConfigureAwait(false);
            Assert.IsGreaterThan(0, guarded.Count);
            if (string.Equals(method, nameof(WarpSourceEndKernels.Catch), StringComparison.Ordinal))
            { Assert.Contains(WarpSourceSegmentGuardedDisposition.OriginUnwound, guarded); }
            if (string.Equals(method, nameof(WarpSourceEndKernels.MultipleFilters), StringComparison.Ordinal))
            { Assert.Contains(WarpSourceSegmentGuardedDisposition.OriginReplacedByBoundAlias, guarded); }
            if (string.Equals(method, nameof(WarpSourceEndKernels.Filter), StringComparison.Ordinal) && flag == 0)
            {
                Assert.AreEqual(WarpLogicalMachineLayout.Faulted, driver.State[0]); Assert.AreEqual(WarpLogicalMachineLayout.ManagedExceptionFault, driver.State[1]);
                CollectionAssert.AreEqual(driver.Owner, driver.State.AsSpan(WarpLogicalMachineLayout.EscapedExceptionContextOffset, 3).ToArray());
            }
            else
            {
                Assert.AreEqual(WarpLogicalMachineLayout.Completed, driver.State[0]);
                uint expected = string.Equals(method, nameof(WarpSourceEndKernels.Catch), StringComparison.Ordinal) ? 0x89ABCDEFu :
                    string.Equals(method, nameof(WarpSourceEndKernels.MultipleFilters), StringComparison.Ordinal) ? 22u : 17u;
                Assert.AreEqual(expected, driver.State[WarpLogicalMachineLayout.ResultOffset]);
                if (string.Equals(method, nameof(WarpSourceEndKernels.Catch), StringComparison.Ordinal)) { Assert.AreEqual(0x81234567u, driver.State[WarpLogicalMachineLayout.ResultHighOffset]); }
            }
            TestContext.WriteLine($"{method}/{flag}: pid={driver.Lease.ProcessId}, module={driver.Lease.CompiledModule}, quantum={quantum}, guarded={string.Join(',', guarded)}, child-quanta={driver.Ordinal}; concrete prepared-nonnull fixture only, no source grant/factory/opaque private invocation.");
        }
    }

    private static async Task<HashSet<WarpSourceSegmentGuardedDisposition>> ExecuteAsync(WarpSourceGuardedChildDriver driver, int quantum)
    {
        HashSet<WarpSourceSegmentGuardedDisposition> guarded = []; WarpSourceSegmentSnapshot? first = null;
        await driver.ExecuteAsync(quantum).ConfigureAwait(false);
        while (driver.State[0] == WarpLogicalMachineLayout.Runnable && driver.Ordinal < 10000)
        {
            if (driver.State[WarpLogicalMachineLayout.SourceBoundaryStateOffset] != WarpLogicalMachineLayout.BeforeSourceBoundary)
            { await driver.ExecuteAsync(quantum).ConfigureAwait(false); continue; }
            int frame = WarpLogicalMachineLayout.HeaderWords + ((int)driver.State[WarpLogicalMachineLayout.DepthOffset] - 1) * driver.Fixture.Map.Layout.FrameWords;
            WarpLogicalMachineNode node = driver.Fixture.Map.Layout.Nodes[(int)driver.State[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset]];
            WarpPortableSourceSegment segment = driver.Fixture.Map.Find(node.Function, node.Block);
            WarpSourceSegmentSnapshot origin = driver.Fixture.Snapshot(driver.Lease, driver.Ordinal, driver.State, driver.Arena); first ??= origin;
            do { await driver.ExecuteAsync(quantum).ConfigureAwait(false); }
            while (driver.State[0] == WarpLogicalMachineLayout.Runnable && driver.State[WarpLogicalMachineLayout.SourceBoundaryStateOffset] != WarpLogicalMachineLayout.BeforeSourceBoundary && driver.Ordinal < 10000);
            WarpSourceSegmentSnapshot end = driver.Fixture.Snapshot(driver.Lease, driver.Ordinal, driver.State, driver.Arena);
            if (end.State[0] == WarpLogicalMachineLayout.Runnable && HasGuardedTarget(driver, segment, end))
            { guarded.Add(ValidateGuarded(driver, segment, origin, end, quantum)); }
            if (end.State[0] == WarpLogicalMachineLayout.Faulted && end.State[1] == WarpLogicalMachineLayout.ManagedExceptionFault)
            { ValidateTerminal(driver, segment, origin, end); }
        }
        Assert.IsLessThan(10000ul, driver.Ordinal); Assert.IsNotNull(first);
        driver.Fixture.WriteWitness("Guarded-" + driver.Fixture.Graph.Methods.First(method => string.Equals(method.Identity, driver.Fixture.Graph.EntryIdentity, StringComparison.Ordinal)).SourceMethod.Name + "-" + driver.Flag.ToString(System.Globalization.CultureInfo.InvariantCulture), driver.Lease,
            quantum, first, first, driver.Fixture.Snapshot(driver.Lease, driver.Ordinal, driver.State, driver.Arena));
        return guarded;
    }

    private static bool HasGuardedTarget(WarpSourceGuardedChildDriver driver, WarpPortableSourceSegment segment, WarpSourceSegmentSnapshot end)
    {
        int frame = WarpLogicalMachineLayout.HeaderWords + ((int)end.State[WarpLogicalMachineLayout.DepthOffset] - 1) * driver.Fixture.Map.Layout.FrameWords;
        return segment.GuardedFrontiers.Any(frontier => frontier.EndFunction == end.State[frame] &&
            frontier.EndProgramCounter == end.State[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset]);
    }

    private static WarpSourceSegmentGuardedDisposition ValidateGuarded(WarpSourceGuardedChildDriver driver, WarpPortableSourceSegment segment,
        WarpSourceSegmentSnapshot origin, WarpSourceSegmentSnapshot end, int quantum)
    {
        int depth = (int)end.State[WarpLogicalMachineLayout.DepthOffset]; int frame = WarpLogicalMachineLayout.HeaderWords + (depth - 1) * driver.Fixture.Map.Layout.FrameWords;
        WarpPortableSourceGuardedFrontier frontier = segment.GuardedFrontiers.First(item => item.EndFunction == end.State[frame] &&
            item.EndProgramCounter == end.State[frame + WarpLogicalMachineLayout.FrameProgramCounterOffset]);
        driver.Fixture.WriteWitness("GuardedCandidate-" + driver.Fixture.Graph.Methods.First(method => string.Equals(method.Identity, driver.Fixture.Graph.EntryIdentity, StringComparison.Ordinal)).SourceMethod.Name + "-" + driver.Flag.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            "-" + end.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture), driver.Lease, quantum, origin, end, end,
            new { segment.Function, segment.CilOffset, segment.SourceOpcode, segment.OriginKind, segment.CapturedMethodIndex, frontier });
        WarpSourceSegmentGuardedDisposition disposition = WarpSourceSegmentGuardedEndContract.ValidateCandidate(driver.Fixture.Map, segment,
            frontier, origin, end, WarpSourceEventFixture.MaximumDepth, end.StateHash, end.ArenaHash);
        ValidateAlias(driver, frontier, origin, end, disposition);
        var original = new WarpSourceSegmentCheckpointContract(driver.Fixture.Map, segment, origin, WarpSourceEventFixture.MaximumDepth, (int)origin.State[WarpLogicalMachineLayout.DepthOffset]);
        if (disposition == WarpSourceSegmentGuardedDisposition.OriginReplacedByBoundAlias)
        { Assert.ThrowsExactly<InvalidOperationException>(() => original.ValidateExactCommittedCandidate(end, origin, end.Ordinal, end.Sequence, end.StateHash, end.ArenaHash)); }
        Assert.ThrowsExactly<InvalidOperationException>(() => driver.Fixture.Map.RequireExactGuardedFrontier(segment, frontier with { }));
        WarpSourceSegmentSnapshot forged = WarpSourceEventFixture.Copy(end, state => state[WarpLogicalMachineLayout.HeaderWords + WarpLogicalMachineLayout.FrameActivationOffset]++);
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpSourceSegmentGuardedEndContract.ValidateCandidate(driver.Fixture.Map, segment,
            frontier, origin, forged, WarpSourceEventFixture.MaximumDepth, forged.StateHash, forged.ArenaHash));
        return disposition;
    }

    private static void ValidateAlias(WarpSourceGuardedChildDriver driver, WarpPortableSourceGuardedFrontier frontier,
        WarpSourceSegmentSnapshot origin, WarpSourceSegmentSnapshot end, WarpSourceSegmentGuardedDisposition disposition)
    {
        if (frontier.AliasOwnerFunction < 0) { return; }
        WarpLogicalMachineLayout layout = driver.Fixture.Map.Layout;
        int depth = (int)end.State[WarpLogicalMachineLayout.DepthOffset]; int frame = WarpLogicalMachineLayout.HeaderWords + (depth - 1) * layout.FrameWords;
        int ownerDepth = (int)end.State[frame + WarpLogicalMachineLayout.FrameAliasOwnerDepthOffset];
        int owner = WarpLogicalMachineLayout.HeaderWords + (ownerDepth - 1) * layout.FrameWords;
        Assert.AreEqual((uint)frontier.AliasOwnerFunction, end.State[owner]);
        Assert.AreEqual(end.State[owner + WarpLogicalMachineLayout.FrameActivationOffset], end.State[frame + WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset]);
        Assert.AreEqual(frontier.AliasPrefixWords, layout.GetAliasPrefixWords(frontier.EndFunction));
        if (disposition == WarpSourceSegmentGuardedDisposition.OriginRetained)
        { Assert.IsGreaterThan((int)origin.State[WarpLogicalMachineLayout.DepthOffset], depth); }
        WarpSourceSegmentSnapshot stale = WarpSourceEventFixture.Copy(end, state => state[frame + WarpLogicalMachineLayout.FrameAliasOwnerActivationOffset]++);
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpSourceSegmentBankContract.Validate(layout, stale.State.AsSpan(), WarpSourceEventFixture.MaximumDepth));
    }

    private static void ValidateTerminal(WarpSourceGuardedChildDriver driver, WarpPortableSourceSegment segment,
        WarpSourceSegmentSnapshot origin, WarpSourceSegmentSnapshot end)
    {
        Assert.AreEqual(WarpSourceSegmentCandidateResult.OpaqueRegistryEndAndPublicationReceiptsRequired,
            WarpSourceSegmentEndContract.ValidateCandidate(driver.Fixture.Map, segment, end, WarpSourceEventFixture.MaximumDepth,
                (int)end.State[WarpLogicalMachineLayout.DepthOffset], WarpPortableSourceSegmentEndKind.ManagedExceptionTerminal,
                WarpSourceSegmentCheckpointContract.Remaining(origin.State.AsSpan())));
        WarpSourceSegmentSnapshot incomplete = WarpSourceEventFixture.Copy(end, state => state[WarpLogicalMachineLayout.EscapedExceptionGenerationOffset] = 0);
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpSourceSegmentEndContract.ValidateCandidate(driver.Fixture.Map, segment, incomplete,
            WarpSourceEventFixture.MaximumDepth, (int)end.State[WarpLogicalMachineLayout.DepthOffset], WarpPortableSourceSegmentEndKind.ManagedExceptionTerminal,
            WarpSourceSegmentCheckpointContract.Remaining(origin.State.AsSpan())));
    }
}
