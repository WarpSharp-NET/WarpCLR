using System.Reflection.Emit;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableClosedFrameSourceBinding
{
    private WarpBranchTerminator AddressStorage(WarpPortableWordInstructionContext context)
    {
        WarpPortableMethodGraphInstruction input = context.SourceInstruction;
        bool argument = input.OpCode.Name!.StartsWith("ldarga", StringComparison.Ordinal);
        int index = checked((int)input.Operand);
        WarpPortableWordStorageSlot slot = (argument ? context.Body.Arguments : context.Body.Locals)[index];
        int[] owner = FrameOwner(context, slot.WordOffset, slot.Type, plan.Schema.TypeId(slot.Type.Identity));
        Store(context, context.StackWordOffset(context.Instruction.EntryStack.Length), owner);
        return context.Next();
    }

    private WarpBranchTerminator ConstructValue(WarpPortableWordInstructionContext context)
    {
        WarpPortableWordPrivateTemporary[] candidates = context.Body.PrivateTemporaries.Where(item => item.SourceOffset == context.Instruction.Offset).Take(2).ToArray();
        if (candidates.Length != 1) { throw WarpPortableClosedFrameSourcePlan.Invalid("The source constructor needs exactly one captured private temporary.", context.Instruction.Offset); }
        WarpPortableWordPrivateTemporary temporary = candidates[0];
        WarpPortableMethodGraphMethod constructor = plan.Graph.Methods.First(method => string.Equals(method.Identity, temporary.ConstructorIdentity, StringComparison.Ordinal));
        if (temporary.Type.Category != WarpPortableStackCategory.Value || !constructor.SourceMethod.DeclaringType!.IsValueType)
        {
            throw WarpPortableClosedFrameSourcePlan.Invalid("This constructor needs its real heap allocation/fault binding.", context.Instruction.Offset);
        }
        int depth = context.Instruction.EntryStack.Length - constructor.ParameterTypes.Length;
        int[][] parameters = Enumerable.Range(0, constructor.ParameterTypes.Length).Select(index =>
            context.LoadStackValue(depth + index).Take(Type(constructor.ParameterTypes[index]).WordCount).ToArray()).ToArray();
        Store(context, temporary.WordOffset, Enumerable.Range(0, temporary.Type.WordCount).Select(_ => context.Constant(0)));
        int[] owner = FrameOwner(context, temporary.WordOffset, temporary.Type, plan.Schema.TypeId(temporary.Type.Identity));
        context.Call(context.SourceFunction(temporary.ConstructorIdentity), owner.Concat(parameters.SelectMany(words => words)), 0);
        Store(context, context.StackWordOffset(depth), Load(context, temporary.WordOffset, temporary.Type.WordCount));
        foreach (WarpPortableWordTemporaryOwner field in temporary.Owners)
        {
            Store(context, field.PrivateWordOffset, Enumerable.Range(0, 3).Select(_ => context.Constant(0)));
        }
        return context.Next();
    }

    private WarpConditionalBranchTerminator Field(WarpPortableWordInstructionContext context)
    {
        WarpPortableSourceHeapField field = plan.Schema.Fields.First(item => string.Equals(item.Identity, context.SourceInstruction.Field, StringComparison.Ordinal));
        WarpPortableTypedType type = Type(context.Instruction.MemoryType!);
        bool store = context.SourceInstruction.OpCode == OpCodes.Stfld;
        int slot = context.Instruction.EntryStack.Length - (store ? 2 : 1);
        WarpPortableTypedType declaring = Type(plan.Schema.Types[(int)field.DeclaringType - 1].Identity);
        return WithOwner(context, declaring, stage => stage.LoadStackValue(slot).ToArray(), (stage, _) =>
        {
            return WithOwner(stage, type, leaf =>
            {
                int[] owner = leaf.LoadStackValue(slot).ToArray();
                owner[3] = Add(leaf, owner[3], (uint)field.SourceByteOffset);
                owner[4] = leaf.Constant((uint)field.ByteSize); owner[5] = leaf.Constant(field.FieldType);
                return owner;
            }, (memory, owner) =>
            {
                if (memory.SourceInstruction.OpCode == OpCodes.Ldflda) { Store(memory, memory.StackWordOffset(slot), owner); }
                else if (store) { WriteValue(memory, owner, type, memory.LoadStackValue(slot + 1).ToArray()); }
                else { Store(memory, memory.StackWordOffset(slot), ReadValue(memory, owner, type)); }
                return memory.Next();
            });
        });
    }
}
