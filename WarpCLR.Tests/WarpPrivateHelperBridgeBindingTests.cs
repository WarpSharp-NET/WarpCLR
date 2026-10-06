using System.Diagnostics.CodeAnalysis;
using System.Reflection.Emit;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers this fixture.")]
internal sealed partial class WarpPrivateHelperBridgeBindingTests
{
    [TestMethod]
    public void CompilerBridgeReloadsOriginalPrivateOperandsAfterItsFirstPrivateLoad()
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Kernel).GetMethod(nameof(Kernel.Echo))!);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        WarpPortableWordLoweredProgram program = WarpPortableWordLowerer.Lower(graph, typed, schema, new Binding(graph, typed, schema, true));
        WarpPortableWordProgramIdentity identity = WarpPortableWordProgramIdentity.Validate(graph, schema, program);
        Assert.AreEqual(WarpPortableWordProgramIdentity.PrivateHelperBoundaryVersion, identity.IdentityVersion, StringComparer.Ordinal);
        WarpPrivateControllerProjection projection = program.Kernel.Execution!.PrivateControllerProjection!;
        Assert.IsTrue(projection.RequiresHelperBoundaries); Assert.HasCount(1, projection.Uses);
        WarpPrivateControllerUse use = projection.Uses[0]; WarpBasicBlock block = program.Kernel.Functions[use.Function - 1].Blocks[use.Block];
        Assert.AreEqual(WarpPrivateControllerOpCode.LoadController, block.Instructions[0].OpCode);
        Assert.AreEqual(WarpManagedFrameOpCode.LoadPrivateWord, block.Instructions[1].OpCode);
        Assert.AreEqual(0, program.Kernel.Execution.Bodies[use.Function].SourceBlockCosts[use.Block]);
        WarpPortableWordSourceBlock original = program.Bodies.First(body => body.Function == use.Function).SourceBlocks.First(source => source.GeneratedBlocks.Contains(use.Block));
        Assert.AreEqual(OpCodes.Ret.Value, original.Instruction.OpCode);
        Assert.AreEqual(1, program.Kernel.Execution.Bodies[use.Function].SourceBlockCosts[original.Block]);
    }

    [TestMethod]
    public void ABridgeApiCannotInventItsMissingConditionalCapability()
    {
        WarpPortableMethodGraph graph = WarpPortableMethodGraph.Discover(typeof(Kernel).GetMethod(nameof(Kernel.Echo))!);
        WarpPortableTypedProgram typed = WarpPortableTypedProgram.Verify(graph);
        WarpPortableSourceHeapSchema schema = WarpPortableSourceHeapSchema.Create(graph, typed);
        Assert.ThrowsExactly<InvalidOperationException>(() => WarpPortableWordLowerer.Lower(graph, typed, schema, new Binding(graph, typed, schema, false)));
    }

    private sealed class Binding : WarpPortableWordExecutionBinding
    {
        private const string Service = "warp.proof.private-bridge/reloaded-u32-operand/0.1";
        private int helper;
        internal Binding(WarpPortableMethodGraph graph, WarpPortableTypedProgram typed, WarpPortableSourceHeapSchema schema, bool boundaries)
            : base(graph.GraphHash, typed.VerifiedHash, schema.SchemaHash, Service, graph.GraphHash,
                new(FrameOwners: true, RuntimeStateAccess: true, PrivateController: true, PrivateHelperBoundaries: boundaries)) { }

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
