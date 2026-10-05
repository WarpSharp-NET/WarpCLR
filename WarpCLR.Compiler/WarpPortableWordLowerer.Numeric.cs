using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableWordLowerer
{
    private sealed partial class MethodLowerer
    {
        private void Numeric(WarpPortableMethodGraphInstruction input, WarpPortableTypedInstruction typed)
        {
            string name = input.OpCode.Name!;
            if (typed.Faults.Any(fault => fault.Kind is WarpPortableTypedFaultKind.Overflow or WarpPortableTypedFaultKind.DivideByZero))
            {
                throw Error("Checked integer faults require generated fault/EH continuation lowering.", input.Offset);
            }
            bool unary = name is "neg" or "not";
            int depth = typed.EntryStack.Length - (unary ? 1 : 2);
            int[] left = StackValue(typed, depth); int[] right = unary ? [] : StackValue(typed, depth + 1);
            WarpPortableStackCategory category = typed.EntryStack[depth].Category;
            if (!unary && typed.EntryStack[depth + 1].Category == WarpPortableStackCategory.CliNativeInteger) { category = WarpPortableStackCategory.CliNativeInteger; }
            if (name is "shl" or "shr" or "shr.un") { right = [right[0]]; }
            else if (!unary && category == WarpPortableStackCategory.CliNativeInteger)
            {
                left = PromoteCliNativeOperand(left); right = PromoteCliNativeOperand(right);
            }
            if (category == WarpPortableStackCategory.Reference && name is "cgt.un" && typed.EntryStack[depth].IsNull)
            {
                Store(StackWord(typed, depth), Constant(0)); return;
            }
            int[] result = name is "ceq" or "cgt" or "cgt.un" or "clt" or "clt.un" ? [Compare(name, category, left, right, input.Offset)] :
                category is WarpPortableStackCategory.Binary32 or WarpPortableStackCategory.Binary64 ? FloatNumeric(name, category, left, right, input.Offset) :
                IntegerNumeric(name, left, right, input.Offset);
            StoreWords(StackWord(typed, depth), result);
        }

        private int[] PromoteCliNativeOperand(int[] words, bool zeroExtend = false) => words.Length == 2 ? words :
            [words[0], zeroExtend ? Constant(0) : Service(typeof(WarpPortableInteger32), "ExtendSignedHigh", words[0])];

        private int[] IntegerNumeric(string name, int[] left, int[] right, int offset)
        {
            WarpIrOpCode? operation = name switch
            {
                "add" => WarpIrOpCode.Add, "sub" => WarpIrOpCode.Subtract, "mul" => WarpIrOpCode.Multiply,
                "and" => WarpIrOpCode.BitwiseAnd, "or" => WarpIrOpCode.BitwiseOr, "xor" => WarpIrOpCode.ExclusiveOr,
                "shl" => WarpIrOpCode.ShiftLeft, "shr.un" => WarpIrOpCode.ShiftRightLogical, _ => null,
            };
            if (name is "not") { return left.Select(word => Emit(WarpIrOpCode.BitwiseNot, word)).ToArray(); }
            if (name is "neg") { return left.Length == 1 ? [Emit(WarpIrOpCode.Subtract, Constant(0), left[0])] : WideService(typeof(WarpPortableInteger64), "Negate", left); }
            if (left.Length == 1)
            {
                if (name is "shr") { return [Service(typeof(WarpPortableInteger32), "ShiftRightSigned", left[0], right[0])]; }
                if (operation is { } simple) { return [Emit(simple, left[0], right[0])]; }
            }
            else
            {
                if (name is "and" or "or" or "xor") { return [Emit(operation!.Value, left[0], right[0]), Emit(operation.Value, left[1], right[1])]; }
                string? service = name switch { "add" => "Add", "sub" => "Subtract", "mul" => "Multiply", "shl" => "ShiftLeft", "shr" => "ShiftRightSigned", "shr.un" => "ShiftRightUnsigned", _ => null };
                if (service is not null) { return WideService(typeof(WarpPortableInteger64), service, [.. left, .. right]); }
            }
            throw Error("An integer operation has no exact common word service: " + name, offset);
        }

        private int[] FloatNumeric(string name, WarpPortableStackCategory category, int[] left, int[] right, int offset)
        {
            string? service = name switch { "add" => "Add", "sub" => "Subtract", "mul" => "Multiply", "div" => "Divide", "rem" => "Remainder", "neg" => "Negate", _ => null };
            if (service is null) { throw Error("A float operation has no strict word service: " + name, offset); }
            bool single = category == WarpPortableStackCategory.Binary32;
            Type implementation = name is "neg" ? typeof(WarpPortableNumericComparisons) : name is "rem" ?
                single ? typeof(WarpPortableBinary32Math) : typeof(WarpPortableBinary64Math) : single ? typeof(WarpPortableBinary32) : typeof(WarpPortableBinary64);
            if (name is "neg") { service = (single ? "Binary32" : "Binary64") + service; }
            return single ? [Service(implementation, service, [.. left, .. right])] : WideService(implementation, service, [.. left, .. right]);
        }
    }
}
