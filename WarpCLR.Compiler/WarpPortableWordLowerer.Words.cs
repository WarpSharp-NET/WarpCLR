using System.Reflection.Emit;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableWordLowerer
{
    private sealed partial class MethodLowerer
    {
        private int Emit(WarpIrOpCode operation, int left = -1, int right = -1, uint immediate = 0, int third = -1)
        {
            int result = value++; instructions.Add(new(result, operation, left, right, immediate, third)); return result;
        }

        private int Constant(uint bits) => Emit(WarpIrOpCode.Constant, immediate: bits);
        private int Load(int word) => Emit(WarpManagedFrameOpCode.LoadPrivateWord, immediate: (uint)word);
        private void Store(int word, int item) => Emit(WarpManagedFrameOpCode.StorePrivateWord, item, immediate: (uint)word);
        private int[] LoadWords(int word, int count) => Enumerable.Range(word, count).Select(Load).ToArray();
        private void StoreWords(int word, int[] items)
        {
            for (int part = 0; part < items.Length; part++) { Store(word + part, items[part]); }
        }

        private int StackWord(WarpPortableTypedInstruction typed, int depth) => stackOffset + typed.EntryStack.Take(depth).Sum(item => item.WordCount);
        private int[] StackValue(WarpPortableTypedInstruction typed, int depth) => LoadWords(StackWord(typed, depth), typed.EntryStack[depth].WordCount);
        private WarpBranchTerminator Branch(int offset) => new(new WarpBranchTarget(sourceBlocks[offset], []));
        private WarpBranchTerminator Next(WarpPortableTypedInstruction typed) => typed.Successors.Length == 1 ? Branch(typed.Successors[0]) :
            throw Error("This instruction requires an explicit generated control rule.", typed.Offset);

        private void InitializeArguments()
        {
            instructions = []; int input = 0;
            foreach (WarpPortableWordStorageSlot slot in arguments)
            {
                int[] items = Enumerable.Range(input, slot.Type.WordCount).Select(index => Emit(WarpIrOpCode.LoadArgument, immediate: (uint)index)).ToArray();
                StoreStorage(slot, items); input += items.Length;
            }
            blocks[0] = new(0, [], instructions, Branch(method.Instructions.First(instruction => instruction.Reachable).Offset));
            InitializeRootInvocation();
        }

        private int[] LoadStorage(WarpPortableWordStorageSlot slot)
        {
            int[] items = LoadWords(slot.WordOffset, slot.Type.WordCount);
            if (slot.Type.Category == WarpPortableStackCategory.I4 && slot.Type.StorageBits < 32)
            {
                uint mask = (1u << slot.Type.StorageBits) - 1; items[0] = Emit(WarpIrOpCode.BitwiseAnd, items[0], Constant(mask));
                if (slot.Type.IsSigned)
                {
                    int sign = Constant(1u << (slot.Type.StorageBits - 1));
                    items[0] = Emit(WarpIrOpCode.Subtract, Emit(WarpIrOpCode.ExclusiveOr, items[0], sign), sign);
                }
            }
            return items;
        }

        private void StoreStorage(WarpPortableWordStorageSlot slot, int[] items)
        {
            if (items.Length > slot.Type.WordCount) { items = items.Take(slot.Type.WordCount).ToArray(); }
            if (slot.Type.Category == WarpPortableStackCategory.I4 && slot.Type.StorageBits < 32)
            {
                items[0] = Emit(WarpIrOpCode.BitwiseAnd, items[0], Constant((1u << slot.Type.StorageBits) - 1));
            }
            StoreWords(slot.WordOffset, items);
        }

        private void PrivateStorage(WarpPortableMethodGraphInstruction input, WarpPortableTypedInstruction typed)
        {
            string name = input.OpCode.Name!;
            if (name.StartsWith("ldarga", StringComparison.Ordinal) || name.StartsWith("ldloca", StringComparison.Ordinal))
            {
                throw Error("Frame byref creation requires its owner activation service.", input.Offset);
            }
            int index = input.OpCode.OperandType is OperandType.InlineVar or OperandType.ShortInlineVar ? checked((int)input.Operand) : name[^1] - '0';
            WarpPortableWordStorageSlot slot = name.Contains("arg", StringComparison.Ordinal) ? arguments[index] : locals[index];
            if (name.StartsWith("st", StringComparison.Ordinal)) { StoreStorage(slot, StackValue(typed, typed.EntryStack.Length - 1)); }
            else { StoreWords(StackWord(typed, typed.EntryStack.Length), LoadStorage(slot)); }
        }
    }
}
