using System.Diagnostics.CodeAnalysis;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;

namespace WarpCLR.Tests;

[TestClass]
[DoNotParallelize]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this fixture.")]
internal sealed class WarpPrivateHelperReturnTupleTests
{
    [TestMethod]
    [DataRow(0, false)] [DataRow(0, true)] [DataRow(1, false)]
    [DataRow(1, true)] [DataRow(4, false)] [DataRow(4, true)]
    public void VoidSingleAndWideReturnsPublishOnlyTheirExactRawTupleBeforeCallerStores(int words, bool largeQuantum)
    {
        WarpLogicalMachineLayout layout = new(Create(words)); CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        int quantum = largeQuantum ? 4096 : layout.MaximumBlockCost; uint[] state = layout.CreateInitialState(8, 100);
        uint[] arena = [0xA5A5A5A5, 0x80000000, 0x7FC12345]; layout.SetSourceBoundaryMode(state, true);
        for (int attempt = 0; attempt < 100 && state[15] != WarpLogicalMachineLayout.NeedsPrivateHelper; attempt++)
        { Invoke(compiled, state, arena, 0, quantum); }
        Assert.AreEqual(WarpLogicalMachineLayout.NeedsPrivateHelper, state[15]); layout.AcknowledgePrivateHelperBoundary(state);
        for (int attempt = 0; attempt < 100 && state[15] != WarpLogicalMachineLayout.AwaitingRootRelease; attempt++)
        { Invoke(compiled, state, arena, WarpPrivateHelperReturnFenceFixture.Controller, quantum); }
        Assert.AreEqual(WarpLogicalMachineLayout.AwaitingRootRelease, state[15]); Assert.AreEqual(1u, state[6]);
        WarpPrivateHelperReturnSite site = layout.PrivateHelperReturnSites.RequireExactlyOne(); Assert.AreEqual(words, site.ResultWordCount);
        Assert.AreEqual(words == 0 ? 0 : 1, site.ResultValue); Assert.AreEqual(100u, state[4]);
        uint[] expected = [WarpPrivateHelperReturnFenceFixture.Controller, 0x7FC12345, 0x80000000, uint.MaxValue];
        CollectionAssert.AreEqual(expected.Take(words).ToArray(), state.AsSpan(64 + 8 + site.ResultValue, words).ToArray());
        Assert.AreEqual(0u, state[64 + layout.PrivateOffset]); Assert.AreEqual(0u, state[7]);
        uint[] before = (uint[])state.Clone(); Invoke(compiled, state, arena, 0, 4096); CollectionAssert.AreEqual(before, state);
        layout.AcknowledgePrivateHelperRelease(state);
        for (int attempt = 0; attempt < 100 && state[0] == WarpLogicalMachineLayout.Runnable; attempt++)
        { Invoke(compiled, state, arena, 0, quantum); }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[0]); Assert.AreEqual(0xBAD0C0DEu, state[64 + layout.PrivateOffset]);
        CollectionAssert.AreEqual(expected.Take(words).ToArray(), Enumerable.Range(0, words).Select(word => state[layout.GetResultWordOffset(word, 8)]).ToArray());
        CollectionAssert.AreEqual(new uint[4], state.AsSpan(28, 4).ToArray()); Assert.AreEqual(100u, state[4]);
        CollectionAssert.AreEqual(new uint[] { WarpPrivateHelperReturnFenceFixture.Controller, 0x80000000, 0x7FC12345 }, arena);
    }

    private static void Invoke(CoreCLRResumableKernel compiled, uint[] state, uint[] arena, uint controller, int quantum) =>
        compiled.CompiledEntryPoint.Invoke(null, [new uint[][] { [0] }, new uint[] { controller }, 0,
            state, 8, quantum, CancellationToken.None, arena]);

    private static WarpControlFlowKernel Create(int words)
    {
        string service = WarpPrivateHelperReturnFenceFixture.Service + "/width/" + words.ToString(System.Globalization.CultureInfo.InvariantCulture);
        int after = 1 + Math.Max(1, words);
        WarpBasicBlock[] root = [new(0, [], [new(0, WarpPrivateControllerOpCode.LoadController), new(1, 0, [0], words),
            new(after, WarpIrOpCode.Constant, immediate: 0xBAD0C0DE), new(after + 1, WarpManagedFrameOpCode.StorePrivateWord, after)],
            new WarpTupleReturnTerminator(Enumerable.Range(1, words)))];
        WarpControlFlowFunction[] functions = [new(0, service, 1, [new(0, [], [new(0, WarpIrOpCode.LoadArgument),
            new(1, WarpIrOpCode.Constant, immediate: 0x7FC12345), new(2, WarpIrOpCode.Constant, immediate: 0x80000000),
            new(3, WarpIrOpCode.Constant, immediate: uint.MaxValue), new(4, WarpIrOpCode.Constant),
            new(5, WarpManagedMemoryOpCode.StoreWord, 4, 0)], new WarpTupleReturnTerminator(Enumerable.Range(0, words)))])];
        var metadata = new WarpLogicalExecutionMetadata([new(1, false, [0]), new(0, true, [0], countsSourceDepth: false)],
            frameOwners: true, runtimeStateAccess: true, privateControllerProjection: new([new(0, 0, 0, 0, 0, 1, service)], true, true));
        return new("controlled-private-helper-raw-return-width", 1, 1, root, null, functions, metadata);
    }
}
