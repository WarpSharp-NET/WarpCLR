using WarpCLR.Tests;
using System.Diagnostics.CodeAnalysis;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest constructs this internal fixture through reflected discovery.")]
internal sealed partial class WarpCapturedVoidPrivateHelperTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CapturedEmptyRetKeepsItsZeroWidthCallAndChargesBeforeTheReleaseFence(bool largeQuantum)
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Guest).GetMethod(nameof(Guest.Empty))!);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        WarpPortableWordLoweredProgram program = WarpPortableWordLowerer.Lower(graph, typed, schema, new VoidBinding(graph, typed, schema));
        var layout = new WarpLogicalMachineLayout(program.Kernel);
        WarpPrivateHelperReturnSite site = layout.PrivateHelperReturnSites.RequireExactlyOne();
        WarpIrInstruction call = layout.Nodes[site.CallProgramCounter].Call!.Value;
        Assert.AreEqual(-1, call.Result); Assert.AreEqual(0, call.ResultWordCount);
        Assert.AreEqual(0, site.ResultWordCount); Assert.AreEqual(0, site.ResultValue);
        AssertVoidCodec(program.Kernel);
        CoreCLRResumableKernel core = CoreCLRResumableKernel.Compile(layout);
        int quantum = largeQuantum ? 4096 : layout.MaximumBlockCost;
        uint[] state = layout.CreateInitialState(8, 100), arena = [0x7FC12345, 0x80000000, uint.MaxValue];
        uint[] originalArena = (uint[])arena.Clone();
        layout.SetSourceBoundaryMode(state, enabled: true);
        AdvanceVoid(core, state, arena, quantum, 0, WarpLogicalMachineLayout.NeedsPrivateHelper);
        Assert.AreEqual(99u, state[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
        layout.AcknowledgePrivateHelperBoundary(state);
        AdvanceVoid(core, state, arena, quantum, 0x81234567, WarpLogicalMachineLayout.AwaitingRootRelease);
        Assert.AreEqual(99u, state[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
        uint[] parked = (uint[])state.Clone();
        InvokeVoid(core, state, arena, quantum, 0);
        CollectionAssert.AreEqual(parked, state); CollectionAssert.AreEqual(originalArena, arena);
        InvokeVoid(core, state, arena, 4096, 0x81234567);
        CollectionAssert.AreEqual(parked, state); CollectionAssert.AreEqual(originalArena, arena);
        // Consistency-only acknowledgment; this test issues no Root release/publication authority.
        layout.AcknowledgePrivateHelperRelease(state);
        AdvanceVoid(core, state, arena, quantum, 0, targetPhase: 0);
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[WarpLogicalMachineLayout.StatusOffset]);
        Assert.AreEqual(99u, state[WarpLogicalMachineLayout.RemainingStepsLowOffset]);
        CollectionAssert.AreEqual(new uint[4], state.AsSpan(WarpLogicalMachineLayout.PrivateHelperScopeCallOffset, 4).ToArray());
        CollectionAssert.AreEqual(originalArena, arena);
    }

    [TestMethod]
    [DataRow(-2)]
    [DataRow(-3)]
    public void APrivateUseRejectsEveryCallMarkerBelowTheVoidSentinel(int marker) =>
        Assert.ThrowsExactly<ArgumentException>(() => new WarpPrivateControllerProjection([new(0, 0, 0, 0, 0, marker, VoidBinding.Service)], true, true));

    [TestMethod]
    public void TheVoidSentinelCannotAuthorizeANonzeroWidthCall()
    {
        WarpBasicBlock[] blocks = [new(0, [], [new(0, WarpPrivateControllerOpCode.LoadController), new(-1, 0, [0], 1)],
            new WarpTupleReturnTerminator([]))];
        WarpControlFlowFunction[] functions = [new(0, VoidBinding.Service, 1,
            [new(0, [], [new(0, WarpIrOpCode.LoadArgument)], new WarpReturnTerminator(0))])];
        var metadata = new WarpLogicalExecutionMetadata([new(0, false, [0]), new(0, true, [0], countsSourceDepth: false)],
            frameOwners: true, runtimeStateAccess: true,
            privateControllerProjection: new([new(0, 0, 0, 0, 0, -1, VoidBinding.Service)], true, true));
        Assert.ThrowsExactly<ArgumentException>(() => new WarpControlFlowKernel("void-marker/nonzero-denial", 0, 1, blocks, null, functions, metadata));
    }

    private static void AdvanceVoid(CoreCLRResumableKernel core, uint[] state,
        uint[] arena, int quantum, uint controller, uint targetPhase)
    {
        for (int attempt = 0; attempt < 100 && state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable; attempt++)
        {
            if (targetPhase != 0 && state[WarpLogicalMachineLayout.SourceBoundaryStateOffset] == targetPhase) { return; }
            if (state[WarpLogicalMachineLayout.SourceBoundaryStateOffset] == WarpLogicalMachineLayout.BeforeSourceBoundary)
            { WarpLogicalMachineLayout.AcknowledgeSourceBoundary(state); }
            InvokeVoid(core, state, arena, quantum, controller);
        }
        if (targetPhase != 0) { Assert.AreEqual(targetPhase, state[WarpLogicalMachineLayout.SourceBoundaryStateOffset]); }
    }

    private static void InvokeVoid(CoreCLRResumableKernel core, uint[] state, uint[] arena, int quantum, uint controller) =>
        core.CompiledEntryPoint.CreateDelegate<Action<uint[][], uint[], int, uint[], int, int, CancellationToken, uint[]>>()
            ([], [controller], 0, state, 8, quantum, CancellationToken.None, arena);

    private static void AssertVoidCodec(WarpControlFlowKernel kernel)
    {
        WarpControlFlowKernel decoded = WarpCoreCLRBinaryPlanCodec.Deserialize(WarpCoreCLRBinaryPlanCodec.Serialize(kernel), WarpIrHash.Compute(kernel));
        Assert.AreEqual(WarpIrHash.Compute(kernel), WarpIrHash.Compute(decoded), StringComparer.Ordinal);
        Assert.AreEqual(-1, decoded.Execution!.PrivateControllerProjection!.Uses.RequireExactlyOne().CallValue);
        Assert.AreEqual(0, new WarpLogicalMachineLayout(decoded).PrivateHelperReturnSites.RequireExactlyOne().ResultWordCount);
    }
}
