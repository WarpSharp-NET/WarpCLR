using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableExceptionSourceBinding
{
    private WarpConditionalBranchTerminator Return(WarpPortableWordInstructionContext context)
    {
        int status = Call(context, nameof(WarpPortableExceptionServices.ReleaseCaughtForReturn), [.. Ticket(context), CurrentFunction(context),
            context.Constant((uint)context.Instruction.Offset), context.Constant(unchecked((ushort)context.Instruction.OpCode))]);
        int complete = context.ReserveGeneratedBlock();
        context.EmitGeneratedBlock(complete, SourceReturn);
        return ContinueOrReject(context, status, complete);
    }

    private static WarpTupleReturnTerminator SourceReturn(WarpPortableWordInstructionContext context)
    {
        WarpPortableTypedType result = context.Program.Types.First(type => string.Equals(type.Identity, context.SourceMethod.ReturnType, StringComparison.Ordinal));
        if (result.Category == WarpPortableStackCategory.Void) { return new([]); }
        int[] words = context.LoadStackValue(0).ToArray();
        if (result.Category == WarpPortableStackCategory.I4 && result.StorageBits < 32)
        {
            words[0] = context.Emit(WarpIrOpCode.BitwiseAnd, words[0], context.Constant((1u << result.StorageBits) - 1));
        }
        return new(words);
    }

    private WarpConditionalBranchTerminator Throw(WarpPortableWordInstructionContext context)
    {
        WarpPortableTypedInstruction source = context.Instruction;
        if (source.EntryStack.Length != 1 || source.EntryStack[0].Category != WarpPortableStackCategory.Reference || source.EntryStack[0].IsNull ||
            source.Effects.Length != 2 || source.Effects[0] != WarpPortableTypedEffect.NullCheck || source.Effects[1] != WarpPortableTypedEffect.Call)
        {
            throw new WarpVerificationException("WRPCLR2300", "This explicit EH domain requires a prepared nonnull exception operand and exact ordered throw effects.", source.Offset);
        }
        int next = context.ReserveGeneratedBlock();
        int captured = Call(context, nameof(WarpPortableExceptionServices.CaptureFrames), Ticket(context));
        context.EmitGeneratedBlock(next, stage =>
        {
            int typed = stage.ReserveGeneratedBlock();
            int[] owner = stage.LoadStackValue(0).ToArray();
            int status = Call(stage, nameof(WarpPortableHeapServices.GetType), owner);
            stage.EmitGeneratedBlock(typed, raising =>
            {
                int type = ArenaLoad(raising, raising.Constant(WarpPortableHeapLayout.Result));
                int raised = Call(raising, nameof(WarpPortableExceptionServices.RaiseReference),
                    [.. Ticket(raising), CurrentFunction(raising), raising.Constant((uint)source.Offset),
                        raising.Constant(unchecked((ushort)source.OpCode)), raising.Constant(1), .. raising.LoadStackValue(0), type]);
                return DriveOrReject(raising, raised);
            });
            return ContinueOrReject(stage, status, typed);
        });
        return ContinueOrReject(context, captured, next);
    }

    private WarpConditionalBranchTerminator Rethrow(WarpPortableWordInstructionContext context) => CaptureAndControl(context, nameof(WarpPortableExceptionServices.Rethrow));
    private WarpConditionalBranchTerminator Leave(WarpPortableWordInstructionContext context) => CaptureAndControl(context, nameof(WarpPortableExceptionServices.BeginLeave));

    private WarpConditionalBranchTerminator CaptureAndControl(WarpPortableWordInstructionContext context, string service)
    {
        int next = context.ReserveGeneratedBlock();
        int captured = Call(context, nameof(WarpPortableExceptionServices.CaptureFrames), Ticket(context));
        context.EmitGeneratedBlock(next, stage =>
        {
            int status = Call(stage, service, [.. Ticket(stage), CurrentFunction(stage),
                stage.Constant((uint)stage.Instruction.Offset), stage.Constant(unchecked((ushort)stage.Instruction.OpCode))]);
            return DriveOrReject(stage, status);
        });
        return ContinueOrReject(context, captured, next);
    }

    private WarpConditionalBranchTerminator EndCleanup(WarpPortableWordInstructionContext context)
    {
        int record = ActiveRecord(context);
        int status = Call(context, nameof(WarpPortableExceptionServices.EndCleanupAt), [.. Ticket(context),
            ArenaLoad(context, Add(context, record, WarpPortableExceptionLayout.RaiseGeneration)),
            ArenaLoad(context, Add(context, record, WarpPortableExceptionLayout.CleanupClause)), CurrentFunction(context),
            context.Constant((uint)context.Instruction.Offset), context.Constant(unchecked((ushort)context.Instruction.OpCode))]);
        return DriveOrReject(context, status);
    }

    private WarpConditionalBranchTerminator DriveOrReject(WarpPortableWordInstructionContext context, int status)
    {
        int next = context.ReserveGeneratedBlock();
        context.EmitGeneratedBlock(next, stage =>
        {
            stage.Call(driving, []);
            return Reject(stage);
        });
        return ContinueOrReject(context, status, next);
    }

    private static WarpConditionalBranchTerminator ContinueOrReject(WarpPortableWordInstructionContext context, int status, int next)
    {
        int rejected = context.ReserveGeneratedBlock(); context.EmitGeneratedBlock(rejected, Reject);
        int valid = context.Emit(WarpIrOpCode.Equal, status, context.Constant(0));
        return new WarpConditionalBranchTerminator(valid, new(next, []), new(rejected, []));
    }

    private static WarpBlockTerminator Reject(WarpPortableWordInstructionContext context)
    {
        context.Emit(WarpManagedStateOpCode.StoreWord, context.Constant(WarpLogicalMachineLayout.FaultKindOffset), context.Constant(3));
        context.Emit(WarpManagedStateOpCode.StoreWord, context.Constant(WarpLogicalMachineLayout.StatusOffset), context.Constant(WarpLogicalMachineLayout.Faulted));
        int words = context.Program.Types.First(type => string.Equals(type.Identity, context.SourceMethod.ReturnType, StringComparison.Ordinal)).WordCount;
        return new WarpStateDispatchTerminator([new(context.Body.Function, context.SourceEntryBlock)], words);
    }

    private int Call(WarpPortableWordInstructionContext context, string name, IEnumerable<int> arguments) => context.Call(entries[name], arguments)[0];

    private int[] Ticket(WarpPortableWordInstructionContext context)
    {
        int scheduler = ArenaLoad(context, context.Constant(WarpPortableSchedulerLayout.HeapDescriptor));
        int participant = context.Emit(WarpManagedInvocationOpCode.LoadLogicalWorker);
        int table = Add(context, scheduler, ArenaLoad(context, Add(context, scheduler, WarpPortableSchedulerLayout.WorkerStart)));
        int row = Add(context, table, context.Emit(WarpIrOpCode.Multiply, participant, context.Constant(WarpPortableSchedulerLayout.WorkerWords)));
        return [context.Constant(controller), participant, ArenaLoad(context, Add(context, row, WarpPortableSchedulerLayout.RunGeneration)),
            ArenaLoad(context, Add(context, scheduler, WarpPortableSchedulerLayout.DispatchGeneration))];
    }

    private static int ActiveRecord(WarpPortableWordInstructionContext context)
    {
        int descriptor = ArenaLoad(context, context.Constant(WarpPortableExceptionLayout.Descriptor));
        int participant = context.Emit(WarpManagedInvocationOpCode.LoadLogicalWorker);
        int row = Add(context, Add(context, descriptor, ArenaLoad(context, Add(context, descriptor, WarpPortableExceptionLayout.WorkerStart))),
            context.Emit(WarpIrOpCode.Multiply, participant, context.Constant(WarpPortableExceptionLayout.WorkerWords)));
        int index = context.Emit(WarpIrOpCode.Subtract, ArenaLoad(context, Add(context, row, WarpPortableExceptionLayout.ActiveRecord)), context.Constant(1));
        int count = ArenaLoad(context, Add(context, descriptor, WarpPortableExceptionLayout.RecordsPerWorker));
        return Add(context, Add(context, descriptor, ArenaLoad(context, Add(context, descriptor, WarpPortableExceptionLayout.RecordStart))),
            context.Emit(WarpIrOpCode.Multiply, Add(context, context.Emit(WarpIrOpCode.Multiply, participant, count), index), context.Constant(WarpPortableExceptionLayout.RecordWords)));
    }

    private static int ArenaLoad(WarpPortableWordInstructionContext context, int word) => context.Emit(WarpManagedMemoryOpCode.LoadWord, word);
    private static int CurrentFunction(WarpPortableWordInstructionContext context)
    {
        int depth = context.Emit(WarpManagedStateOpCode.LoadWord, context.Constant(WarpLogicalMachineLayout.DepthOffset));
        int stride = context.Emit(WarpManagedStateOpCode.LoadWord, context.Constant(WarpLogicalMachineLayout.FrameStrideOffset));
        int frame = Add(context, context.Constant(WarpLogicalMachineLayout.HeaderWords),
            context.Emit(WarpIrOpCode.Multiply, context.Emit(WarpIrOpCode.Subtract, depth, context.Constant(1)), stride));
        return context.Emit(WarpManagedStateOpCode.LoadWord, frame);
    }
    private static int Add(WarpPortableWordInstructionContext context, int word, uint offset) => Add(context, word, context.Constant(offset));
    private static int Add(WarpPortableWordInstructionContext context, int first, int second) => context.Emit(WarpIrOpCode.Add, first, second);
}
