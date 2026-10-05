using System.Reflection.Emit;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableClosedFrameSourceBinding
{
    private WarpConditionalBranchTerminator Indirect(WarpPortableWordInstructionContext context)
    {
        OpCode operation = context.SourceInstruction.OpCode;
        WarpPortableTypedType type = Type(context.Instruction.MemoryType!);
        bool initialize = operation == OpCodes.Initobj;
        bool copy = operation == OpCodes.Cpobj;
        bool store = initialize || copy || operation == OpCodes.Stobj || operation.Name!.StartsWith("stind.", StringComparison.Ordinal);
        int slot = context.Instruction.EntryStack.Length - (store && !initialize ? 2 : 1);
        if (copy)
        {
            return WithOwner(context, type, read => read.LoadStackValue(slot + 1).ToArray(), (read, _) =>
                WithOwner(read, type, write => write.LoadStackValue(slot).ToArray(), (write, owner) =>
            {
                WriteValue(write, owner, type, ReadValue(write, write.LoadStackValue(slot + 1).ToArray(), type)); return write.Next();
            }));
        }
        return WithOwner(context, type, stage => stage.LoadStackValue(slot).ToArray(), (stage, owner) =>
        {
            if (store)
            {
                int[] values = initialize ? Enumerable.Range(0, type.WordCount).Select(_ => stage.Constant(0)).ToArray() : stage.LoadStackValue(slot + 1).ToArray();
                WriteValue(stage, owner, type, values);
            }
            else
            {
                bool? signed = operation == OpCodes.Ldind_I1 || operation == OpCodes.Ldind_I2 ? true :
                    operation == OpCodes.Ldind_U1 || operation == OpCodes.Ldind_U2 ? false : null;
                Store(stage, stage.StackWordOffset(slot), ReadValue(stage, owner, type, signed));
            }
            return stage.Next();
        });
    }

    private static int[] ReadValue(WarpPortableWordInstructionContext context, int[] owner, WarpPortableTypedType type, bool? signed = null)
    {
        int payload = Payload(context, owner);
        int[] words = Enumerable.Range(0, type.WordCount).Select(_ => context.Constant(0)).ToArray();
        for (int part = 0; part < type.ByteSize; part++)
        {
            int offset = Add(context, owner[3], (uint)part);
            int address = context.Emit(WarpIrOpCode.Add, payload, context.Emit(WarpIrOpCode.ShiftRightLogical, offset, context.Constant(2)));
            int shift = context.Emit(WarpIrOpCode.Multiply, And(context, offset, context.Constant(3)), context.Constant(8));
            int item = And(context, context.Emit(WarpIrOpCode.ShiftRightLogical, StateLoad(context, address), shift), context.Constant(255));
            words[part / 4] = context.Emit(WarpIrOpCode.BitwiseOr, words[part / 4],
                context.Emit(WarpIrOpCode.ShiftLeft, item, context.Constant((uint)(part % 4 * 8))));
        }
        if (type.Category == WarpPortableStackCategory.I4 && type.StorageBits < 32 && (signed ?? type.IsSigned))
        {
            int sign = context.Constant(1u << (type.StorageBits - 1));
            words[0] = context.Emit(WarpIrOpCode.Subtract, context.Emit(WarpIrOpCode.ExclusiveOr, words[0], sign), sign);
        }
        return words;
    }

    private static void WriteValue(WarpPortableWordInstructionContext context, int[] owner, WarpPortableTypedType type, int[] words)
    {
        int payload = Payload(context, owner);
        for (int part = 0; part < type.ByteSize; part++)
        {
            int offset = Add(context, owner[3], (uint)part);
            int address = context.Emit(WarpIrOpCode.Add, payload, context.Emit(WarpIrOpCode.ShiftRightLogical, offset, context.Constant(2)));
            int shift = context.Emit(WarpIrOpCode.Multiply, And(context, offset, context.Constant(3)), context.Constant(8));
            int item = And(context, context.Emit(WarpIrOpCode.ShiftRightLogical, words[part / 4], context.Constant((uint)(part % 4 * 8))), context.Constant(255));
            int mask = context.Emit(WarpIrOpCode.ExclusiveOr, context.Emit(WarpIrOpCode.ShiftLeft, context.Constant(255), shift), context.Constant(uint.MaxValue));
            StateStore(context, address, context.Emit(WarpIrOpCode.BitwiseOr, And(context, StateLoad(context, address), mask), context.Emit(WarpIrOpCode.ShiftLeft, item, shift)));
        }
    }
}
