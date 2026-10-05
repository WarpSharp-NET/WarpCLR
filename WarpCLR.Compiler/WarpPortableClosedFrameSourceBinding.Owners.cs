using System.Reflection;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableClosedFrameSourceBinding
{
    private WarpConditionalBranchTerminator WithOwner(WarpPortableWordInstructionContext context,
        WarpPortableTypedType type, Func<WarpPortableWordInstructionContext, int[]> loadOwner,
        Func<WarpPortableWordInstructionContext, int[], WarpBlockTerminator> implementation)
    {
        if (validateOwner < 0)
        {
            MethodInfo method = typeof(WarpPortableFrameServices).GetMethod(nameof(WarpPortableFrameServices.ValidateOwner), BindingFlags.Public | BindingFlags.Static)!;
            validateOwner = context.Services.Import(method, WarpPortableFrameServices.Semantics, WarpPortableGeneratedServiceKind.State).Function;
        }
        int[] owner = loadOwner(context);
        if (owner.Length != 6) { throw WarpPortableClosedFrameSourcePlan.Invalid("A private source owner must retain six exact words.", context.Instruction.Offset); }
        int status = context.Call(validateOwner, owner)[0];
        int check = context.ReserveGeneratedBlock(), completed = context.ReserveGeneratedBlock(), rejected = context.ReserveGeneratedBlock();
        context.EmitGeneratedBlock(rejected, RejectCorruptOwner);
        context.EmitGeneratedBlock(completed, stage => implementation(stage, loadOwner(stage)));
        context.EmitGeneratedBlock(check, stage =>
        {
            // The word IR requires block-local SSA. Private source operands survive
            // helper calls/branches; each fixed continuation reloads its own values.
            int[] captured = loadOwner(stage);
            int frame = FrameStart(stage, captured[1]);
            int function = StateLoad(stage, Add(stage, frame, (uint)WarpLogicalMachineLayout.FrameFunctionOffset));
            int words = StateLoad(stage, Add(stage, frame, (uint)WarpLogicalMachineLayout.FramePrivateWordsOffset));
            int valid = stage.Constant(0);
            uint element = plan.Schema.TypeId(type.Identity);
            foreach (WarpPortableSourceFrameBody body in frames)
            {
                int matches = stage.Emit(WarpIrOpCode.Equal, function, stage.Constant(body.Function));
                matches = And(stage, matches, stage.Emit(WarpIrOpCode.Equal, words, stage.Constant(body.PrivateWords)));
                foreach (WarpPortableSourceFrameView view in body.Views.Where(view => view.ElementType == element && view.ByteSpan == type.ByteSize))
                {
                    valid = stage.Emit(WarpIrOpCode.BitwiseOr, valid, And(stage, matches,
                        stage.Emit(WarpIrOpCode.Equal, captured[3], stage.Constant(view.ByteOffset))));
                }
            }
            valid = And(stage, valid, stage.Emit(WarpIrOpCode.Equal, captured[4], stage.Constant((uint)type.ByteSize)));
            valid = And(stage, valid, stage.Emit(WarpIrOpCode.Equal, captured[5], stage.Constant(element)));
            return new WarpConditionalBranchTerminator(valid, new(completed, []), new(rejected, []));
        });
        int successful = context.Emit(WarpIrOpCode.Equal, status, context.Constant(0));
        return new WarpConditionalBranchTerminator(successful, new(check, []), new(rejected, []));
    }

    private static WarpBlockTerminator RejectCorruptOwner(WarpPortableWordInstructionContext context)
    {
        // The finite domain proves source null owners impossible. This rejects
        // corrupted machine capabilities; it is never a catchable-source-fault replacement.
        StateStore(context, context.Constant(WarpLogicalMachineLayout.FaultKindOffset), context.Constant(3));
        StateStore(context, context.Constant(WarpLogicalMachineLayout.FaultFunctionOffset), context.Constant((uint)context.Body.Function));
        StateStore(context, context.Constant(WarpLogicalMachineLayout.FaultBlockOffset), context.Constant((uint)context.SourceEntryBlock));
        StateStore(context, context.Constant(WarpLogicalMachineLayout.StatusOffset), context.Constant(WarpLogicalMachineLayout.Faulted));
        int resultWords = context.Program.Types.First(type => string.Equals(type.Identity, context.SourceMethod.ReturnType, StringComparison.Ordinal)).WordCount;
        return new WarpStateDispatchTerminator([new(context.Body.Function, context.SourceEntryBlock)], resultWords);
    }

    private static int FrameStart(WarpPortableWordInstructionContext context, int frame)
    {
        int ordinal = context.Emit(WarpIrOpCode.Subtract, frame, context.Constant(1));
        int stride = StateLoad(context, context.Constant(WarpLogicalMachineLayout.FrameStrideOffset));
        return Add(context, context.Emit(WarpIrOpCode.Multiply, ordinal, stride), WarpLogicalMachineLayout.HeaderWords);
    }

    private static int Payload(WarpPortableWordInstructionContext context, int[] owner) =>
        context.Emit(WarpIrOpCode.Add, FrameStart(context, owner[1]), StateLoad(context, context.Constant(WarpLogicalMachineLayout.PrivateBaseOffset)));

    private static int Add(WarpPortableWordInstructionContext context, int left, uint right) => context.Emit(WarpIrOpCode.Add, left, context.Constant(right));
    private static int And(WarpPortableWordInstructionContext context, int left, int right) => context.Emit(WarpIrOpCode.BitwiseAnd, left, right);
    private static int StateLoad(WarpPortableWordInstructionContext context, int address) => context.Emit(WarpManagedStateOpCode.LoadWord, address);
    private static void StateStore(WarpPortableWordInstructionContext context, int address, int value) => context.Emit(WarpManagedStateOpCode.StoreWord, address, value);
}
