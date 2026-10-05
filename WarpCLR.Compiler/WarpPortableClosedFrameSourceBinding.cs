using System.Collections.Immutable;
using System.Reflection.Emit;
using System.Security.Cryptography;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableClosedFrameSourceBinding : WarpPortableWordExecutionBinding
{
    private readonly WarpPortableClosedFrameSourcePlan plan;
    private ImmutableArray<WarpPortableSourceFrameBody> frames = [];
    private int validateOwner = -1;

    internal WarpPortableClosedFrameSourceBinding(WarpPortableClosedFrameSourcePlan plan)
        : base(plan?.Graph.GraphHash ?? throw new ArgumentNullException(nameof(plan)), plan.Program.VerifiedHash,
            plan.Schema.SchemaHash, WarpPortableClosedFrameSourcePlan.Semantics, plan.PlanHash,
            new(FrameOwners: true, RuntimeStateAccess: true, NonlocalStateDispatch: true), plan.Methods)
    {
        this.plan = plan;
    }

    internal override void Prepare(WarpPortableWordBindingPreparation context)
    {
        frames = context.SourceBodies.Select(body => WarpPortableSourceFrameSchema.DescribePlannedBody(plan.Schema, plan.Program, body)).ToImmutableArray();
        context.RequireService(WarpPortableClosedFrameSourcePlan.Semantics + "/" + plan.PlanHash);
        context.RequireService(WarpPortableSourceFrameSchema.Semantics);
    }

    internal override WarpBlockTerminator? LowerInstruction(WarpPortableWordInstructionContext context)
    {
        if (context.Body.AliasOwnerFunction != -1) { throw WarpPortableClosedFrameSourcePlan.Invalid("A frame-only source emitter cannot grant alias evaluation/temporary owner addresses.", context.Instruction.Offset); }
        OpCode operation = context.SourceInstruction.OpCode;
        string name = operation.Name!;
        if (context.Instruction.EntryStack.Length >= 2 && context.Instruction.EntryStack[^1].Category == WarpPortableStackCategory.ManagedByref &&
            (operation == OpCodes.Ceq || operation == OpCodes.Beq || operation == OpCodes.Beq_S || operation == OpCodes.Bne_Un || operation == OpCodes.Bne_Un_S))
        {
            return CompareOwners(context);
        }
        if (name.StartsWith("ldarga", StringComparison.Ordinal) || name.StartsWith("ldloca", StringComparison.Ordinal)) { return AddressStorage(context); }
        if (operation == OpCodes.Newobj && context.Instruction.RequiredIntrinsic?.Contains("value-tuple.construct", StringComparison.Ordinal) != true) { return ConstructValue(context); }
        if (operation == OpCodes.Ldflda || operation == OpCodes.Stfld || operation == OpCodes.Ldfld && context.Instruction.EntryStack[^1].Category == WarpPortableStackCategory.ManagedByref)
        {
            return Field(context);
        }
        if (operation == OpCodes.Ldobj || operation == OpCodes.Stobj || operation == OpCodes.Initobj || operation == OpCodes.Cpobj ||
            name.StartsWith("ldind.", StringComparison.Ordinal) || name.StartsWith("stind.", StringComparison.Ordinal)) { return Indirect(context); }
        return null;
    }

    internal override string Complete(WarpPortableWordBindingCompletion context) =>
        Convert.ToHexString(SHA256.HashData(WarpPortableSnapshotIdentity.Serialize(new
        {
            WarpPortableClosedFrameSourcePlan.Semantics, plan.PlanHash, Frames = frames, context.MapsHash,
            LayoutProjection = WarpPortableWordProgramIdentity.ComputeLayoutProjection(context.StructuralKernel),
            Imports = context.Imports.Select(import => import.Identity),
        })));

    private static int[] FrameOwner(WarpPortableWordInstructionContext context, int word, WarpPortableTypedType type, uint typeId) =>
        [context.Emit(WarpManagedFrameOpCode.OwnerContext), context.Emit(WarpManagedFrameOpCode.OwnerFrame),
         context.Emit(WarpManagedFrameOpCode.OwnerGeneration), context.Constant(checked((uint)word * 4)),
         context.Constant((uint)type.ByteSize), context.Constant(typeId)];

    private static void Store(WarpPortableWordInstructionContext context, int offset, IEnumerable<int> values)
    {
        foreach (int value in values) { context.StorePrivateWord(offset++, value); }
    }

    private static int[] Load(WarpPortableWordInstructionContext context, int offset, int count) => Enumerable.Range(offset, count).Select(context.LoadPrivateWord).ToArray();
    private WarpPortableTypedType Type(string identity) => plan.Program.Types.First(type => string.Equals(type.Identity, identity, StringComparison.Ordinal));
}
