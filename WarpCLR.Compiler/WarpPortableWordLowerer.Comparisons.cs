using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableWordLowerer
{
    private sealed partial class MethodLowerer
    {
        private int Compare(string operation, WarpPortableStackCategory category, int[] left, int[] right, int offset)
        {
            string name = operation switch { "ceq" or "beq" => "eq", "bne.un" => "ne", "cgt" or "bgt" => "gt", "cgt.un" or "bgt.un" => "gt.un", "clt" or "blt" => "lt", "clt.un" or "blt.un" => "lt.un", "bge" => "ge", "bge.un" => "ge.un", "ble" => "le", "ble.un" => "le.un", _ => throw Error("A comparison has no semantic rule.", offset) };
            if (category is WarpPortableStackCategory.Binary32 or WarpPortableStackCategory.Binary64) { return FloatCompare(name, category, left, right); }
            int equal = Emit(WarpIrOpCode.Equal, left[0], right[0]);
            for (int word = 1; word < left.Length; word++) { equal = Emit(WarpIrOpCode.BitwiseAnd, equal, Emit(WarpIrOpCode.Equal, left[word], right[word])); }
            if (name is "eq") { return equal; }
            if (name is "ne") { return Emit(WarpIrOpCode.ExclusiveOr, equal, Constant(1)); }
            if (category == WarpPortableStackCategory.Reference)
            {
                if (name is "gt.un") { return Emit(WarpIrOpCode.ExclusiveOr, equal, Constant(1)); }
                throw Error("A reference comparison requires a registered identity rule.", offset);
            }
            if (category == WarpPortableStackCategory.ManagedByref) { throw Error("Byref identity requires its owner activation ABI.", offset); }
            bool unsigned = name.EndsWith(".un", StringComparison.Ordinal);
            int less = left.Length == 1 ? unsigned ? Emit(WarpIrOpCode.LessThanUnsigned, left[0], right[0]) : Service(typeof(WarpPortableInteger32), "LessThanSigned", left[0], right[0]) :
                Service(typeof(WarpPortableInteger64), unsigned ? "LessThanUnsigned" : "LessThanSigned", [.. left, .. right]);
            if (name.StartsWith("lt", StringComparison.Ordinal)) { return less; }
            if (name.StartsWith("ge", StringComparison.Ordinal)) { return Emit(WarpIrOpCode.ExclusiveOr, less, Constant(1)); }
            int inclusive = Emit(WarpIrOpCode.BitwiseOr, less, equal);
            return name.StartsWith("le", StringComparison.Ordinal) ? inclusive : Emit(WarpIrOpCode.ExclusiveOr, inclusive, Constant(1));
        }

        private int FloatCompare(string name, WarpPortableStackCategory category, int[] left, int[] right)
        {
            string prefix = category == WarpPortableStackCategory.Binary32 ? "Binary32" : "Binary64";
            string operation = name switch { "eq" => "Equal", "ne" => "NotEqual", "lt" => "Less", "lt.un" => "LessOrUnordered", "gt" => "Greater", "gt.un" => "GreaterOrUnordered", "ge" or "ge.un" => "GreaterOrEqual", "le" or "le.un" => "LessOrEqual", _ => throw new InvalidOperationException("The float predicate is missing.") };
            int[] operands = [.. left, .. right];
            int result = Service(typeof(WarpPortableNumericComparisons), prefix + operation, operands);
            if (name is "ge.un" or "le.un") { result = Emit(WarpIrOpCode.BitwiseOr, result, Service(typeof(WarpPortableNumericComparisons), prefix + "Unordered", operands)); }
            return result;
        }

        private int Truth(WarpPortableTypedValue descriptor, int[] words)
        {
            int count = descriptor.Category is WarpPortableStackCategory.Reference or WarpPortableStackCategory.ManagedByref ? 3 : words.Length;
            int result = words[0];
            foreach (ref readonly int word in words.AsSpan(1, count - 1)) { result = Emit(WarpIrOpCode.BitwiseOr, result, word); }
            return result;
        }
    }
}
