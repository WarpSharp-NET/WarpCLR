using System.Reflection.Emit;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableWordLowerer
{
    private sealed partial class MethodLowerer
    {
        private WarpBlockTerminator Control(WarpPortableMethodGraphInstruction input, WarpPortableTypedInstruction typed)
        {
            string name = input.OpCode.Name!;
            if (input.OpCode == OpCodes.Switch) { return Switch(input, typed); }
            if (input.OpCode.FlowControl == FlowControl.Branch)
            {
                if (!typed.UnwindRegions.IsEmpty) { throw Error("Leave must execute its generated finally chain.", input.Offset); }
                return Branch(typed.Successors[0]);
            }
            int branch = sourceBlocks[input.BranchTargets[0]]; int fallthrough = sourceBlocks[input.NextOffset];
            if (branch == fallthrough) { return new WarpBranchTerminator(new(branch, [])); }
            int predicate;
            if (name.StartsWith("brtrue", StringComparison.Ordinal) || name.StartsWith("brfalse", StringComparison.Ordinal))
            {
                int depth = typed.EntryStack.Length - 1;
                predicate = Truth(typed.EntryStack[depth], StackValue(typed, depth));
                if (name.StartsWith("brfalse", StringComparison.Ordinal)) { (branch, fallthrough) = (fallthrough, branch); }
            }
            else
            {
                int depth = typed.EntryStack.Length - 2;
                string comparison = name.EndsWith(".s", StringComparison.Ordinal) ? name[..^2] : name;
                WarpPortableStackCategory category = typed.EntryStack[depth].Category;
                int[] left = StackValue(typed, depth), right = StackValue(typed, depth + 1);
                if (category == WarpPortableStackCategory.CliNativeInteger || typed.EntryStack[depth + 1].Category == WarpPortableStackCategory.CliNativeInteger)
                {
                    // The captured CoreCLR64 profile extends unsigned branch operands differently
                    // from cgt.un/clt.un. Preserve the original branch opcode before common comparison.
                    bool zeroExtend = comparison.EndsWith(".un", StringComparison.Ordinal);
                    category = WarpPortableStackCategory.CliNativeInteger;
                    left = PromoteCliNativeOperand(left, zeroExtend); right = PromoteCliNativeOperand(right, zeroExtend);
                }
                predicate = Compare(comparison, category, left, right, input.Offset);
            }
            return new WarpConditionalBranchTerminator(predicate, new(branch, []), new(fallthrough, []));
        }

        private WarpBlockTerminator Switch(WarpPortableMethodGraphInstruction input, WarpPortableTypedInstruction typed)
        {
            int selector = StackValue(typed, typed.EntryStack.Length - 1)[0];
            int fallback = sourceBlocks[input.NextOffset];
            if (input.BranchTargets.IsEmpty) { return new WarpBranchTerminator(new(fallback, [])); }
            int[] chains = Enumerable.Range(1, input.BranchTargets.Length - 1).Select(_ => ExtraBlock(input.Offset)).ToArray();
            WarpBlockTerminator? first = null;
            for (int index = 0; index < input.BranchTargets.Length; index++)
            {
                List<WarpIrInstruction> original = instructions;
                if (index != 0) { instructions = [new(value++, WarpManagedFrameOpCode.LoadPrivateWord, immediate: (uint)StackWord(typed, typed.EntryStack.Length - 1))]; selector = instructions[0].Result; }
                int destination = sourceBlocks[input.BranchTargets[index]];
                int next = index < chains.Length ? chains[index] : fallback;
                WarpBlockTerminator branch = destination == next ? new WarpBranchTerminator(new(destination, [])) :
                    new WarpConditionalBranchTerminator(Emit(WarpIrOpCode.Equal, selector, Constant((uint)index)), new(destination, []), new(next, []));
                if (index == 0) { first = branch; }
                else { blocks[chains[index - 1]] = new(chains[index - 1], [], instructions, branch); instructions = original; }
            }
            return first!;
        }

        private int ExtraBlock(int sourceOffset)
        {
            WarpCompilationAdmission.Require(method.Identity, WarpCompilationResourceKind.Blocks, blocks.Count + 1L, WarpCompilationAdmission.MaximumBlocksPerEntry);
            int block = blocks.Count;
            blocks.Add(null); charges.Add(0);
            if (!generatedBlocks.TryGetValue(sourceOffset, out List<int>? aliases))
            {
                aliases = []; generatedBlocks.Add(sourceOffset, aliases);
            }
            aliases.Add(block);
            return block;
        }
    }
}
