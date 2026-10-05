using System.Reflection;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed class WarpPortableClosedExceptionAccessorBinding : WarpPortableWordExecutionBinding
{
    private readonly WarpPortableClosedExceptionAccessorPlan plan;
    private int read = -1;
    private int acknowledge = -1;
    private int write = -1;
    private int getType = -1;

    internal WarpPortableClosedExceptionAccessorBinding(WarpPortableClosedExceptionAccessorPlan plan)
        : base(plan?.Graph.GraphHash ?? throw new ArgumentNullException(nameof(plan)), plan.Program.VerifiedHash,
            plan.Schema.SchemaHash, WarpPortableClosedExceptionAccessorPlan.Semantics, plan.PlanHash,
            new(FrameOwners: true, RuntimeStateAccess: true, NonlocalStateDispatch: true), plan.Methods)
    {
        this.plan = plan;
    }

    internal override void Prepare(WarpPortableWordBindingPreparation context)
    {
        read = Import(context, nameof(WarpPortableHeapServices.ReadSourceExceptionAccessor));
        acknowledge = Import(context, nameof(WarpPortableHeapServices.AcknowledgeServiceResult));
        if (plan.Accessors.Any(row => row.VirtualCall)) { getType = Import(context, nameof(WarpPortableHeapServices.GetType)); }
        if (plan.Accessors.Any(row => row.Kind == WarpPortableExceptionAccessorKind.HResultStore)) { write = Import(context, nameof(WarpPortableHeapServices.WriteSourceExceptionHResult)); }
        context.RequireService(WarpPortableClosedExceptionAccessorPlan.Semantics + "/" + plan.PlanHash);
    }

    internal override WarpBlockTerminator? LowerInstruction(WarpPortableWordInstructionContext context)
    {
        WarpPortableClosedExceptionAccessor? row = plan.Accessors.FirstOrDefault(row =>
            string.Equals(row.MethodIdentity, context.SourceMethod.Identity, StringComparison.Ordinal) && row.SourceOffset == context.Instruction.Offset);
        if (row is null) { return null; }
        if (!string.Equals(row.InstructionHash, WarpPortableSnapshotIdentity.Hash(context.Instruction), StringComparison.Ordinal) ||
            !string.Equals(row.TargetIdentity, context.SourceInstruction.Method, StringComparison.Ordinal))
        {
            throw WarpPortableClosedExceptionAccessorPlan.Invalid("The accessor site differs from its captured immutable typed row.", context.Instruction.Offset);
        }
        bool store = row.Kind == WarpPortableExceptionAccessorKind.HResultStore;
        int stack = context.Instruction.EntryStack.Length - (store ? 2 : 1);
        int[] reference = context.LoadStackValue(stack).ToArray();
        int[] arguments = store ? [.. reference, context.LoadStackValue(stack + 1)[0]] : [.. reference, context.Constant(row.DeclaringType), context.Constant((uint)row.Kind)];
        int completed = context.ReserveGeneratedBlock(); int rejected = context.ReserveGeneratedBlock();
        context.EmitGeneratedBlock(rejected, stage =>
        {
            // Corrupt or unadmitted invocation data cannot publish an ordinary result.
            // This finite internal domain is distinct from general catchable source null faults.
            stage.Emit(WarpManagedStateOpCode.StoreWord, stage.Constant(WarpLogicalMachineLayout.FaultKindOffset), stage.Constant(3));
            stage.Emit(WarpManagedStateOpCode.StoreWord, stage.Constant(WarpLogicalMachineLayout.FaultFunctionOffset), stage.Constant((uint)stage.Body.Function));
            stage.Emit(WarpManagedStateOpCode.StoreWord, stage.Constant(WarpLogicalMachineLayout.FaultBlockOffset), stage.Constant((uint)stage.SourceEntryBlock));
            stage.Emit(WarpManagedStateOpCode.StoreWord, stage.Constant(WarpLogicalMachineLayout.StatusOffset), stage.Constant(WarpLogicalMachineLayout.Faulted));
            int count = stage.Program.Types.First(type => string.Equals(type.Identity, stage.SourceMethod.ReturnType, StringComparison.Ordinal)).WordCount;
            return new WarpStateDispatchTerminator([new(stage.Body.Function, stage.SourceEntryBlock)], count);
        });
        context.EmitGeneratedBlock(completed, stage =>
        {
            int count = store ? 0 : row.Kind == WarpPortableExceptionAccessorKind.HResult ? 1 : 3;
            for (int word = 0; word < count; word++)
            {
                int result = stage.Emit(WarpManagedMemoryOpCode.LoadWord, stage.Constant(WarpPortableHeapLayout.Result + (uint)word));
                stage.StorePrivateWord(stage.StackWordOffset(stack) + word, result);
            }
            int owner = stage.Emit(WarpManagedMemoryOpCode.LoadWord, stage.Constant(WarpPortableHeapLayout.LeaseOwner));
            stage.Call(acknowledge, [owner]);
            return stage.Next();
        });
        if (!row.VirtualCall) { return Access(context, arguments, store, completed, rejected); }
        int status = context.Call(getType, reference)[0];
        int actual = context.Emit(WarpManagedMemoryOpCode.LoadWord, context.Constant(WarpPortableHeapLayout.Result));
        int allowed = context.Constant(0);
        foreach (uint type in row.ExactDataTargetTypes)
        {
            allowed = context.Emit(WarpIrOpCode.BitwiseOr, allowed, context.Emit(WarpIrOpCode.Equal, actual, context.Constant(type)));
        }
        int execute = context.ReserveGeneratedBlock();
        context.EmitGeneratedBlock(execute, stage =>
        {
            // Every generated block owns its SSA values. The source private bank
            // retains the exact typed receiver across the completed type service.
            int[] receiver = stage.LoadStackValue(stack).ToArray();
            int[] operands = store ? [.. receiver, stage.LoadStackValue(stack + 1)[0]] :
                [.. receiver, stage.Constant(row.DeclaringType), stage.Constant((uint)row.Kind)];
            return Access(stage, operands, store, completed, rejected);
        });
        return new WarpConditionalBranchTerminator(context.Emit(WarpIrOpCode.BitwiseAnd, allowed,
            context.Emit(WarpIrOpCode.Equal, status, context.Constant(0))), new(execute, []), new(rejected, []));
    }

    internal override string Complete(WarpPortableWordBindingCompletion context) => WarpPortableSnapshotIdentity.Hash(new
    {
        WarpPortableClosedExceptionAccessorPlan.Semantics, plan.PlanHash, context.MapsHash,
        Layout = WarpPortableWordProgramIdentity.ComputeLayoutProjection(context.StructuralKernel),
        Bodies = context.Bodies, context.EntryProjection, Imports = context.Imports.Select(import => import.Identity),
    });

    private static int Import(WarpPortableWordBindingPreparation context, string name) => context.Services.Import(
        typeof(WarpPortableHeapServices).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!,
        WarpPortableHeapServices.ExceptionAccessorSemantics, WarpPortableGeneratedServiceKind.Arena).Function;

    private WarpConditionalBranchTerminator Access(WarpPortableWordInstructionContext context, int[] arguments, bool store, int completed, int rejected)
    {
        int status = context.Call(store ? write : read, arguments)[0];
        return new(context.Emit(WarpIrOpCode.Equal, status, context.Constant(0)), new(completed, []), new(rejected, []));
    }
}
