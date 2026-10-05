using System.Reflection;
using System.Reflection.Emit;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableWordLowerer
{
    private sealed partial class MethodLowerer
    {
        private void Call(WarpPortableMethodGraphInstruction input, WarpPortableTypedInstruction typed)
        {
            WarpPortableMethodGraphMethod target = owner.Sources[input.Method!];
            bool construction = input.OpCode == OpCodes.Newobj;
            int argumentsCount = target.ParameterTypes.Length + (!construction && !target.SourceMethod.IsStatic ? 1 : 0);
            int depth = typed.EntryStack.Length - argumentsCount;
            string[] declared = !construction && !target.SourceMethod.IsStatic ?
                [owner.Program.Methods.First(method => string.Equals(method.Identity, target.Identity, StringComparison.Ordinal)).ArgumentTypes[0], .. target.ParameterTypes] :
                target.ParameterTypes.ToArray();
            int[][] parameters = Enumerable.Range(0, argumentsCount).Select(index =>
                StackValue(typed, depth + index).Take(owner.Types[declared[index]].WordCount).ToArray()).ToArray();
            int[] operands = parameters.SelectMany(words => words).ToArray();
            if (target.Intrinsic is { } intrinsic)
            {
                owner.Services.Add(intrinsic);
                if (intrinsic.Contains("numeric.bit-cast.", StringComparison.Ordinal)) { StoreWords(StackWord(typed, depth), operands); return; }
                if (intrinsic.Contains("value-tuple.construct", StringComparison.Ordinal) && construction) { TupleConstructor(typed, depth, parameters); return; }
                if (intrinsic.Contains("object.reference-equality", StringComparison.Ordinal)) { Store(StackWord(typed, depth), Compare("ceq", WarpPortableStackCategory.Reference, parameters[0], parameters[1], input.Offset)); return; }
                if (intrinsic.Contains("object.base-constructor", StringComparison.Ordinal))
                {
                    if (construction) { throw Error("Object construction requires its generated portable owner allocation service.", input.Offset); }
                    return;
                }
                if (intrinsic.Contains("math.strict.", StringComparison.Ordinal)) { MathIntrinsic(target, typed, depth, operands); return; }
                throw Error("The intrinsic requires a registered generated execution service: " + intrinsic, input.Offset);
            }
            if (construction) { throw Error("Object/value construction requires its portable owner allocation service.", input.Offset); }
            owner.CheckInitializer(target, input.Offset);
            int results = owner.Types[target.ReturnType].WordCount;
            int result = results == 0 ? -1 : value; value = checked(value + results);
            instructions.Add(new(result, owner.FunctionIds[target.Identity], operands, results));
            if (results != 0)
            {
                RecordReturnedRoots(typed, result, owner.Types[target.ReturnType]);
                var storage = new WarpPortableWordStorageSlot(0, StackWord(typed, depth), owner.Types[target.ReturnType]);
                StoreStorage(storage, Enumerable.Range(result, results).ToArray());
                StoreWords(storage.WordOffset, LoadStorage(storage));
            }
        }

        private void TupleConstructor(WarpPortableTypedInstruction typed, int depth, int[][] parameters)
        {
            WarpPortableTypedType tuple = owner.Types[typed.ExitStack[^1].TypeIdentity];
            int destination = StackWord(typed, depth);
            StoreWords(destination, Enumerable.Range(0, tuple.WordCount).Select(_ => Constant(0)).ToArray());
            Dictionary<string, WarpPortableMethodGraphField> captured = owner.Graph.Fields.ToDictionary(field => field.Identity, StringComparer.Ordinal);
            for (int index = 0; index < parameters.Length; index++)
            {
                string name = index == 7 ? "Rest" : "Item" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                WarpPortableTypedField field = tuple.Fields.First(member => string.Equals(captured[member.Identity].SourceField.Name, name, StringComparison.Ordinal));
                StoreBytes(destination, field.ByteOffset, field.ByteSize, parameters[index]);
            }
        }

        private void MathIntrinsic(WarpPortableMethodGraphMethod target, WarpPortableTypedInstruction typed, int depth, int[] operands)
        {
            WarpPortableWordMathBinding binding = WarpPortableWordMathCatalog.Resolve((MethodInfo)target.SourceMethod) ??
                throw Error("The exact math overload requires a complete generated word/fault binding.", typed.Offset);
            owner.Services.Add(binding.Identity);
            if (binding.FaultEntrypoint is not null)
            {
                throw Error("This exact math binding requires the portable source raise/EH continuation: " + binding.Identity, typed.Offset);
            }
            int operand = 0;
            foreach (string identity in target.ParameterTypes)
            {
                WarpPortableTypedType parameter = owner.Types[identity];
                if (parameter.Category == WarpPortableStackCategory.I4 && parameter.StorageBits < 32)
                {
                    int item = Emit(WarpIrOpCode.BitwiseAnd, operands[operand], Constant((1u << parameter.StorageBits) - 1));
                    if (parameter.IsSigned)
                    {
                        int sign = Constant(1u << (parameter.StorageBits - 1)); item = Emit(WarpIrOpCode.Subtract, Emit(WarpIrOpCode.ExclusiveOr, item, sign), sign);
                    }
                    operands[operand] = item;
                }
                operand += parameter.WordCount;
            }
            int[] values = [.. operands, .. binding.AdditionalValueWords.Select(Constant)];
            int[] result = binding.ValueEntrypoints.Select(entry => Service(binding.Implementation, entry, values)).ToArray();
            var storage = new WarpPortableWordStorageSlot(0, StackWord(typed, depth), owner.Types[target.ReturnType]);
            StoreStorage(storage, result); StoreWords(storage.WordOffset, LoadStorage(storage));
        }

        private void StoreBytes(int destination, int byteOffset, int byteCount, int[] words)
        {
            for (int part = 0; part < byteCount; part++)
            {
                int shift = Constant((uint)((part % 4) * 8));
                int item = Emit(WarpIrOpCode.BitwiseAnd, Emit(WarpIrOpCode.ShiftRightLogical, words[part / 4], shift), Constant(255));
                int target = destination + (byteOffset + part) / 4;
                int targetShift = (byteOffset + part) % 4 * 8;
                int cleared = Emit(WarpIrOpCode.BitwiseAnd, Load(target), Constant(~(255u << targetShift)));
                Store(target, Emit(WarpIrOpCode.BitwiseOr, cleared, Emit(WarpIrOpCode.ShiftLeft, item, Constant((uint)targetShift))));
            }
        }
    }
}
