using System.Reflection.Emit;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableClosedInitializerSourceBinding
{
    private WarpBlockTerminator Field(WarpPortableWordInstructionContext context)
    {
        WarpPortableSourceHeapField field = plan.Schema.Fields.First(item => string.Equals(item.Identity, context.SourceInstruction.Field, StringComparison.Ordinal));
        WarpPortableTypedType type = plan.Program.Types.First(item => string.Equals(item.Identity, context.Instruction.MemoryType, StringComparison.Ordinal));
        bool store = context.SourceInstruction.OpCode == OpCodes.Stsfld;
        int start = context.Instruction.EntryStack.Length - (store ? 1 : 0);
        int scratch = context.Emit(WarpManagedMemoryOpCode.LoadWord, context.Constant(WarpPortableHeapLayout.ScratchStart));
        if (store)
        {
            int[] values = context.LoadStackValue(start).ToArray();
            for (int word = 0; word < type.WordCount; word++)
            {
                context.Emit(WarpManagedMemoryOpCode.StoreWord, context.Emit(WarpIrOpCode.Add, scratch, context.Constant((uint)word)), values[word]);
            }
        }
        int heapContext = context.Emit(WarpManagedMemoryOpCode.LoadWord, context.Constant(WarpPortableHeapLayout.Context));
        int[] args = [heapContext, context.Constant(0), context.Constant(field.DeclaringType),
            context.Constant((uint)field.HeapByteOffset), context.Constant((uint)field.ByteSize), context.Constant(field.FieldType), context.Constant(0)];
        if (!store) { args = [.. args, context.Constant(0)]; }
        int status = context.Call(store ? write : read, args)[0];
        int denied = Reject(context), complete = context.ReserveGeneratedBlock();
        context.EmitGeneratedBlock(complete, stage =>
        {
            if (!store)
            {
                int buffer = stage.Emit(WarpManagedMemoryOpCode.LoadWord, stage.Constant(WarpPortableHeapLayout.ScratchStart));
                for (int word = 0; word < type.WordCount; word++)
                {
                    int value = stage.Emit(WarpManagedMemoryOpCode.LoadWord, stage.Emit(WarpIrOpCode.Add, buffer, stage.Constant((uint)word)));
                    if (type.Category == WarpPortableStackCategory.I4 && type.StorageBits < 32 && type.IsSigned)
                    {
                        int sign = stage.Constant(1u << (type.StorageBits - 1));
                        value = stage.Emit(WarpIrOpCode.Subtract, stage.Emit(WarpIrOpCode.ExclusiveOr, value, sign), sign);
                    }
                    stage.StorePrivateWord(stage.StackWordOffset(start) + word, value);
                }
            }
            return stage.Next();
        });
        return IfZero(context, status, complete, denied);
    }
}
