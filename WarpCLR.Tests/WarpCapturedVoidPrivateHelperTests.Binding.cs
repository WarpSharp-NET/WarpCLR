using System.Reflection.Emit;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpCapturedVoidPrivateHelperTests
{
    private sealed class VoidBinding : WarpPortableWordExecutionBinding
    {
        internal const string Service = "warp.production.private-helper/captured-empty-ret/0.1";
        private int helper;

        internal VoidBinding(WarpPortableMethodGraph graph, WarpPortableTypedProgram typed, WarpPortableSourceHeapSchema schema)
            : base(graph.GraphHash, typed.VerifiedHash, schema.SchemaHash, Service, graph.GraphHash,
                new(FrameOwners: true, RuntimeStateAccess: true, PrivateController: true,
                    PrivateHelperBoundaries: true, PrivateHelperReturnFences: true)) { }

        internal override void Prepare(WarpPortableWordBindingPreparation context)
        {
            helper = context.ReserveFunction(Service);
            context.InstallFunction(new(helper, Service, 1,
                [new(0, [], [new(0, WarpIrOpCode.LoadArgument)], new WarpTupleReturnTerminator([]))]),
                new(0, true, [0], countsSourceDepth: false));
        }

        internal override WarpBlockTerminator? LowerInstruction(WarpPortableWordInstructionContext context)
        {
            if (context.SourceInstruction.OpCode != OpCodes.Ret) { return null; }
            int bridge = context.EmitPrivateHelperBridge((stage, controller) =>
            {
                _ = stage.Call(helper, [controller], resultWords: 0);
                return new WarpTupleReturnTerminator([]);
            });
            return new WarpBranchTerminator(new(bridge, []));
        }

        internal override string Complete(WarpPortableWordBindingCompletion context) => WarpIrHash.Compute(context.StructuralKernel);
    }

    private static class Guest
    {
        public static void Empty() { }
    }
}
