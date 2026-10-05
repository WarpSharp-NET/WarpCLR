using System.Reflection.Emit;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableClosedFrameSourceBinding
{
    private WarpConditionalBranchTerminator CompareOwners(WarpPortableWordInstructionContext context)
    {
        int slot = context.Instruction.EntryStack.Length - 2;
        WarpPortableTypedType type = Type(Type(context.Instruction.EntryStack[slot].TypeIdentity).ElementType!);
        return WithOwner(context, type, first => first.LoadStackValue(slot).ToArray(), (first, _) =>
            WithOwner(first, type, second => second.LoadStackValue(slot + 1).ToArray(), (second, _) =>
        {
            int[] left = second.LoadStackValue(slot).ToArray(), right = second.LoadStackValue(slot + 1).ToArray();
            int equal = second.Constant(1);
            // CLI identity compares the address/owner, not span/readonly/type metadata.
            for (int word = 0; word < 4; word++) { equal = And(second, equal, second.Emit(WarpIrOpCode.Equal, left[word], right[word])); }
            if (second.SourceInstruction.OpCode == OpCodes.Ceq)
            {
                second.StorePrivateWord(second.StackWordOffset(slot), equal); return second.Next();
            }
            bool inverse = second.SourceInstruction.OpCode == OpCodes.Bne_Un || second.SourceInstruction.OpCode == OpCodes.Bne_Un_S;
            int condition = inverse ? second.Emit(WarpIrOpCode.Equal, equal, second.Constant(0)) : equal;
            return new WarpConditionalBranchTerminator(condition, new(second.SourceBlock(second.SourceInstruction.BranchTargets[0]), []),
                new(second.SourceBlock(second.SourceInstruction.NextOffset), []));
        }));
    }
}
