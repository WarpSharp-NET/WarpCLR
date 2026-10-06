using System.Diagnostics.CodeAnalysis;
using System.Reflection.Emit;
using WarpCLR.Backend.CoreCLR;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests;

[TestClass]
[DoNotParallelize]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this fixture.")]
internal sealed class WarpPrivateHelperReturnFenceBindingTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void OriginalChargedCilReturnHasAnUnchargedFencedPrivateBridge(bool largeQuantum)
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Kernel).GetMethod(nameof(Kernel.Echo))!);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        WarpPortableWordLoweredProgram program = WarpPortableWordLowerer.Lower(graph, typed, schema, new Binding(graph, typed, schema));
        WarpPortableWordProgramIdentity identity = WarpPortableWordProgramIdentity.Validate(graph, schema, program);
        Assert.AreEqual(WarpPortableWordProgramIdentity.PrivateHelperReturnFenceVersion, identity.IdentityVersion, StringComparer.Ordinal);
        var layout = new WarpLogicalMachineLayout(program.Kernel); CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        int quantum = largeQuantum ? 4096 : layout.MaximumBlockCost; uint[] state = layout.CreateInitialState(8, 100);
        uint[] arena = [0x7FC12345, 0x80000000]; layout.SetSourceBoundaryMode(state, true);
        for (int attempt = 0; attempt < 100 && state[15] != WarpLogicalMachineLayout.NeedsPrivateHelper; attempt++)
        {
            if (state[15] == WarpLogicalMachineLayout.BeforeSourceBoundary) { WarpLogicalMachineLayout.AcknowledgeSourceBoundary(state); }
            Invoke(compiled, state, arena, 0, quantum);
        }
        Assert.AreEqual(WarpLogicalMachineLayout.NeedsPrivateHelper, state[15]); Assert.AreEqual(98u, state[4]);
        layout.AcknowledgePrivateHelperBoundary(state);
        for (int attempt = 0; attempt < 100 && state[15] != WarpLogicalMachineLayout.AwaitingRootRelease; attempt++)
        { Invoke(compiled, state, arena, 0x81234567, quantum); }
        Assert.AreEqual(WarpLogicalMachineLayout.AwaitingRootRelease, state[15]); Assert.AreEqual(WarpLogicalMachineLayout.Runnable, state[0]);
        Assert.AreEqual(98u, state[4]); Assert.AreEqual(0u, state[7]);
        AssertRetainedReturnSite(layout, state);
        uint[] before = (uint[])state.Clone(); uint[] arenaBefore = (uint[])arena.Clone();
        Invoke(compiled, state, arena, 0, 4096); CollectionAssert.AreEqual(before, state); CollectionAssert.AreEqual(arenaBefore, arena);
        layout.AcknowledgePrivateHelperRelease(state);
        for (int attempt = 0; attempt < 100 && state[0] == WarpLogicalMachineLayout.Runnable; attempt++)
        { Invoke(compiled, state, arena, 0, quantum); }
        Assert.AreEqual(WarpLogicalMachineLayout.Completed, state[0]); Assert.AreEqual(0x7FFFFFFFu, state[7]);
        Assert.AreEqual(98u, state[4]); CollectionAssert.AreEqual(arenaBefore, arena);
    }

    private static void AssertRetainedReturnSite(WarpLogicalMachineLayout layout, uint[] state)
    {
        WarpPrivateHelperReturnSite site = layout.PrivateHelperReturnSites.RequireExactlyOne();
        int frame = WarpLogicalMachineLayout.HeaderWords + checked((int)(state[6] - 1) * layout.FrameWords);
        Assert.AreEqual((uint)site.CallerFunction, state[frame]); Assert.AreEqual((uint)site.Continuation, state[frame + 1]);
        Assert.AreEqual(0x7FFFFFFFu, state[frame + WarpLogicalMachineLayout.FrameHeaderWords + site.ResultValue]);
    }

    private static void Invoke(CoreCLRResumableKernel compiled, uint[] state, uint[] arena, uint controller, int quantum) =>
        compiled.CompiledEntryPoint.Invoke(null, [new uint[][] { [0xFEDCBA98] }, new uint[] { controller }, 0,
            state, 8, quantum, CancellationToken.None, arena]);

    private sealed class Binding : WarpPortableWordExecutionBinding
    {
        private const string Service = "warp.proof.return-fence/original-cil-echo-bridge/0.1";
        private int helper;
        internal Binding(WarpPortableMethodGraph graph, WarpPortableTypedProgram typed, WarpPortableSourceHeapSchema schema)
            : base(graph.GraphHash, typed.VerifiedHash, schema.SchemaHash, Service, graph.GraphHash,
                new(FrameOwners: true, RuntimeStateAccess: true, PrivateController: true, PrivateHelperBoundaries: true,
                    PrivateHelperReturnFences: true)) { }

        internal override void Prepare(WarpPortableWordBindingPreparation context)
        {
            helper = context.ReserveFunction(Service);
            context.InstallFunction(new(helper, Service, 2, [new(0, [], [new(0, WarpIrOpCode.LoadArgument),
                new(1, WarpIrOpCode.LoadArgument, immediate: 1), new(2, WarpIrOpCode.ExclusiveOr, 0, 1)], new WarpReturnTerminator(2))]),
                new(0, true, [0], countsSourceDepth: false));
        }

        internal override WarpBlockTerminator? LowerInstruction(WarpPortableWordInstructionContext context)
        {
            if (context.SourceInstruction.OpCode != OpCodes.Ret) { return null; }
            int bridge = context.EmitPrivateHelperBridge((stage, controller) =>
                new WarpReturnTerminator(stage.Call(helper, [controller, stage.LoadStackValue(0)[0]])[0]));
            return new WarpBranchTerminator(new(bridge, []));
        }

        internal override string Complete(WarpPortableWordBindingCompletion context) => WarpIrHash.Compute(context.StructuralKernel);
    }

    private static class Kernel
    {
        public static uint Echo(uint value) => value;
    }
}

