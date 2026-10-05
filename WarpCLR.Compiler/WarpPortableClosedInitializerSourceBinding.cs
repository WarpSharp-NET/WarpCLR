using System.Reflection;
using System.Reflection.Emit;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableClosedInitializerSourceBinding : WarpPortableWordExecutionBinding
{
    private readonly WarpPortableClosedInitializerSourcePlan plan;
    internal int BeginFunction { get; private set; } = -1;
    internal int CompleteFunction { get; private set; } = -1;
    private int read = -1;
    private int write = -1;

    internal WarpPortableClosedInitializerSourceBinding(WarpPortableClosedInitializerSourcePlan plan)
        : base(plan?.Graph.GraphHash ?? throw new ArgumentNullException(nameof(plan)), plan.Program.VerifiedHash,
            plan.Schema.SchemaHash, WarpPortableClosedInitializerSourcePlan.Semantics, plan.PlanHash,
            new(FrameOwners: true, RuntimeStateAccess: true, NonlocalStateDispatch: true, LogicalWorkerAccess: true), plan.Methods)
    {
        this.plan = plan;
    }

    internal WarpPortableClosedInitializerSourcePlan Plan => plan;
    internal bool BindsMethod(string method) => plan.BindsMethod(method);

    internal override void Prepare(WarpPortableWordBindingPreparation context)
    {
        BeginFunction = Import(context, nameof(WarpPortableHeapServices.BeginSourceTypeInitialization), mixed: true);
        CompleteFunction = Import(context, nameof(WarpPortableHeapServices.CompleteSourceTypeInitialization), mixed: true);
        read = Import(context, nameof(WarpPortableHeapServices.ReadSourceValue), mixed: false);
        write = Import(context, nameof(WarpPortableHeapServices.WriteSourceValue), mixed: false);
        context.RequireService(WarpPortableWordInvocationPrelude.Semantics);
        context.RequireService(WarpPortableClosedInitializerSourcePlan.Semantics + "/" + plan.PlanHash);
    }

    internal override WarpBlockTerminator? LowerInstruction(WarpPortableWordInstructionContext context)
    {
        if (context.SourceInstruction.Field is not null) { return TriggerThen(context, Field); }
        if (context.Instruction.InitializerTrigger is not null) { return TriggerThen(context, CallOriginal); }
        return null;
    }

    internal override string Complete(WarpPortableWordBindingCompletion context) => WarpPortableSnapshotIdentity.Hash(new
    {
        WarpPortableClosedInitializerSourcePlan.Semantics, plan.PlanHash, context.MapsHash,
        Layout = WarpPortableWordProgramIdentity.ComputeLayoutProjection(context.StructuralKernel),
        context.Bodies, context.EntryProjection, Imports = context.Imports.Select(import => import.Identity),
    });

    private static int Import(WarpPortableWordBindingPreparation context, string name, bool mixed)
    {
        MethodInfo method = typeof(WarpPortableHeapServices).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;
        return context.Services.Import(method, WarpPortableHeapServices.SourceInitializationSemantics,
            mixed ? WarpPortableGeneratedServiceKind.StateAndArena : WarpPortableGeneratedServiceKind.Arena,
            mixed ? WarpPortableSourceServiceBanks.Capture(method) : null).Function;
    }

    private WarpBlockTerminator TriggerThen(WarpPortableWordInstructionContext context,
        Func<WarpPortableWordInstructionContext, WarpBlockTerminator> remaining)
    {
        WarpPortableTypedInitializerTrigger? trigger = context.Instruction.InitializerTrigger;
        if (trigger is null) { return remaining(context); }
        uint type = plan.Schema.TypeId(trigger.DeclaringType);
        int denied = Reject(context), inspect = context.ReserveGeneratedBlock(), invoke = context.ReserveGeneratedBlock();
        int complete = context.ReserveGeneratedBlock(), proceed = context.ReserveGeneratedBlock();
        context.EmitGeneratedBlock(proceed, remaining);
        context.EmitGeneratedBlock(complete, stage =>
        {
            int worker = stage.Emit(WarpManagedInvocationOpCode.LoadLogicalWorker);
            int status = stage.Call(CompleteFunction, [stage.Constant(type), worker])[0];
            return IfZero(stage, status, proceed, denied);
        });
        context.EmitGeneratedBlock(invoke, stage =>
        {
            stage.Call(stage.SourceFunction(trigger.Initializer), [], 0);
            return new WarpBranchTerminator(new(complete, []));
        });
        context.EmitGeneratedBlock(inspect, stage =>
        {
            int decision = stage.Emit(WarpManagedMemoryOpCode.LoadWord, stage.Constant(WarpPortableHeapLayout.Result));
            int cached = stage.ReserveGeneratedBlock();
            stage.EmitGeneratedBlock(cached, cachedStage =>
            {
                int state = cachedStage.Emit(WarpManagedMemoryOpCode.LoadWord, cachedStage.Constant(WarpPortableHeapLayout.Result));
                int valid = cachedStage.Emit(WarpIrOpCode.BitwiseOr, cachedStage.Emit(WarpIrOpCode.Equal, state, cachedStage.Constant(2)),
                    cachedStage.Emit(WarpIrOpCode.Equal, state, cachedStage.Constant(3)));
                return new WarpConditionalBranchTerminator(valid, new(proceed, []), new(denied, []));
            });
            return new WarpConditionalBranchTerminator(stage.Emit(WarpIrOpCode.Equal, decision, stage.Constant(1)), new(invoke, []), new(cached, []));
        });
        int logicalWorker = context.Emit(WarpManagedInvocationOpCode.LoadLogicalWorker);
        int begun = context.Call(BeginFunction, [context.Constant(type), logicalWorker])[0];
        return IfZero(context, begun, inspect, denied);
    }

    private static WarpConditionalBranchTerminator IfZero(WarpPortableWordInstructionContext context, int value, int yes, int no) =>
        new(context.Emit(WarpIrOpCode.Equal, value, context.Constant(0)), new(yes, []), new(no, []));

    private static int Reject(WarpPortableWordInstructionContext context)
    {
        int denied = context.ReserveGeneratedBlock();
        context.EmitGeneratedBlock(denied, stage =>
        {
            stage.Emit(WarpManagedStateOpCode.StoreWord, stage.Constant(WarpLogicalMachineLayout.FaultKindOffset), stage.Constant(3));
            stage.Emit(WarpManagedStateOpCode.StoreWord, stage.Constant(WarpLogicalMachineLayout.FaultFunctionOffset), stage.Constant((uint)stage.Body.Function));
            stage.Emit(WarpManagedStateOpCode.StoreWord, stage.Constant(WarpLogicalMachineLayout.FaultBlockOffset), stage.Constant((uint)stage.SourceEntryBlock));
            stage.Emit(WarpManagedStateOpCode.StoreWord, stage.Constant(WarpLogicalMachineLayout.StatusOffset), stage.Constant(WarpLogicalMachineLayout.Faulted));
            int count = stage.Program.Types.First(type => string.Equals(type.Identity, stage.SourceMethod.ReturnType, StringComparison.Ordinal)).WordCount;
            return new WarpStateDispatchTerminator([new(stage.Body.Function, stage.SourceEntryBlock)], count);
        });
        return denied;
    }

    private static WarpBlockTerminator CallOriginal(WarpPortableWordInstructionContext context)
    {
        WarpPortableMethodGraphMethod target = context.Graph.Methods.First(method => string.Equals(method.Identity, context.SourceInstruction.Method, StringComparison.Ordinal));
        int start = context.Instruction.EntryStack.Length - target.ParameterTypes.Length;
        int[] parameters = Enumerable.Range(0, target.ParameterTypes.Length).SelectMany(index => context.LoadStackValue(start + index)).ToArray();
        int count = context.Program.Types.First(type => string.Equals(type.Identity, target.ReturnType, StringComparison.Ordinal)).WordCount;
        int[] result = context.Call(context.SourceFunction(target.Identity), parameters, count).ToArray();
        for (int word = 0; word < count; word++) { context.StorePrivateWord(context.StackWordOffset(start) + word, result[word]); }
        return context.Next();
    }
}
