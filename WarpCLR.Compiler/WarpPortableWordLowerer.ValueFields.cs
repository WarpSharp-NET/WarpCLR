using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableWordLowerer
{
    private sealed partial class MethodLowerer
    {
        private void ValueField(WarpPortableMethodGraphInstruction input, WarpPortableTypedInstruction typed)
        {
            WarpPortableTypedValue receiver = typed.EntryStack[^1];
            if (receiver.Category != WarpPortableStackCategory.Value)
            {
                throw Error("Heap/byref field execution requires its generated owner and fault bindings.", input.Offset);
            }
            WarpPortableMethodGraphField captured = owner.Graph.Fields.First(field => string.Equals(field.Identity, input.Field, StringComparison.Ordinal));
            WarpPortableTypedField field = owner.Types[captured.DeclaringType].Fields.First(item => string.Equals(item.Identity, captured.Identity, StringComparison.Ordinal));
            WarpPortableTypedType storage = owner.Types[field.TypeIdentity];
            int[] result = ExtractFieldBytes(StackValue(typed, typed.EntryStack.Length - 1), field.ByteOffset, storage);
            if (storage.Category == WarpPortableStackCategory.I4 && storage.StorageBits < 32 && storage.IsSigned)
            {
                int sign = Constant(1u << (storage.StorageBits - 1));
                result[0] = Emit(WarpIrOpCode.Subtract, Emit(WarpIrOpCode.ExclusiveOr, result[0], sign), sign);
            }
            StoreWords(StackWord(typed, typed.EntryStack.Length - 1), result);
        }

        private int[] ExtractFieldBytes(int[] sourceWords, int byteOffset, WarpPortableTypedType storage)
        {
            WarpCompilationAdmission.Require(method.Identity, WarpCompilationResourceKind.Instructions,
                storage.WordCount * 12L, WarpCompilationAdmission.MaximumInstructionsPerEntry);
            int[] result = new int[storage.WordCount];
            for (int word = 0; word < result.Length; word++)
            {
                int offset = checked(byteOffset + word * 4);
                int shift = (offset & 3) * 8;
                int item = sourceWords[offset / 4];
                if (shift != 0) { item = Emit(WarpIrOpCode.ShiftRightLogical, item, Constant((uint)shift)); }
                int bytes = Math.Min(4, storage.ByteSize - word * 4);
                if (shift != 0 && bytes * 8 > 32 - shift)
                {
                    int next = Emit(WarpIrOpCode.ShiftLeft, sourceWords[offset / 4 + 1], Constant((uint)(32 - shift)));
                    item = Emit(WarpIrOpCode.BitwiseOr, item, next);
                }
                if (bytes < 4) { item = Emit(WarpIrOpCode.BitwiseAnd, item, Constant((1u << (bytes * 8)) - 1)); }
                result[word] = item;
            }
            return result;
        }
    }
}
