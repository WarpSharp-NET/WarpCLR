using System.Reflection.Emit;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableWordLowerer
{
    private sealed partial class MethodLowerer
    {
        private readonly Dictionary<int, WarpPortableStackCategory> directUnsignedSingles = [];
        private int scratchOffset;
        private const string UnsignedSingleTransform = "warp.cil.unsigned-single-adjacent/rne-direct-no-double-round/0.1";

        private void PrepareUnsignedSingleConversions()
        {
            foreach (WarpPortableTypedInstruction typed in method.Instructions.Where(instruction => instruction.Reachable && instruction.OpCode == OpCodes.Conv_R_Un.Value))
            {
                WarpPortableTypedInstruction? next = method.Instructions.FirstOrDefault(instruction => instruction.Offset == typed.NextOffset);
                if (next?.OpCode == OpCodes.Conv_R4.Value && method.Instructions.Count(instruction => instruction.Reachable && instruction.Successors.Contains(next.Offset)) == 1)
                {
                    directUnsignedSingles.Add(next.Offset, typed.EntryStack[^1].Category);
                    owner.Services.Add(UnsignedSingleTransform);
                }
            }
            scratchOffset = checked(stackOffset + method.MaximumStackWords);
        }

        private void Conversion(WarpPortableMethodGraphInstruction input, WarpPortableTypedInstruction typed)
        {
            string name = input.OpCode.Name!;
            if (name.Contains("ovf", StringComparison.Ordinal)) { throw Error("Checked conversions require generated overflow/EH fault dispatch.", input.Offset); }
            int depth = typed.EntryStack.Length - 1; int[] words = StackValue(typed, depth);
            WarpPortableStackCategory sourceType = typed.EntryStack[depth].Category;
            if (directUnsignedSingles.TryGetValue(input.Offset, out WarpPortableStackCategory original))
            {
                words = LoadWords(scratchOffset, original == WarpPortableStackCategory.I4 ? 1 : 2);
                Store(StackWord(typed, depth), Service(typeof(WarpPortableNumericConversions), original == WarpPortableStackCategory.I4 ? "UInt32ToSingle" : "UInt64ToSingle", words)); return;
            }
            if (input.OpCode == OpCodes.Conv_R_Un && directUnsignedSingles.ContainsKey(input.NextOffset)) { StoreWords(scratchOffset, words); }
            int[] result = name is "conv.r4" or "conv.r8" or "conv.r.un" ? ToFloat(name, sourceType, words) :
                sourceType is WarpPortableStackCategory.Binary32 or WarpPortableStackCategory.Binary64 ? FloatToInteger(name, sourceType, words, input.Offset) : IntegerConversion(name, words);
            StoreWords(StackWord(typed, depth), result);
        }

        private int[] IntegerConversion(string name, int[] words)
        {
            if (name is "conv.i8" or "conv.u8" or "conv.i" or "conv.u")
            {
                return words.Length == 2 ? words : [words[0], name is "conv.u8" or "conv.u" ? Constant(0) : Service(typeof(WarpPortableInteger32), "ExtendSignedHigh", words[0])];
            }
            string? extend = name switch { "conv.i1" => "ExtendSigned8", "conv.u1" => "ExtendUnsigned8", "conv.i2" => "ExtendSigned16", "conv.u2" => "ExtendUnsigned16", _ => null };
            return [extend is null ? words[0] : Service(typeof(WarpPortableInteger32), extend, words[0])];
        }

        private int[] ToFloat(string name, WarpPortableStackCategory category, int[] words)
        {
            bool single = name is "conv.r4";
            if (category == WarpPortableStackCategory.Binary32) { return single ? words : WideService(typeof(WarpPortableNumericConversions), "SingleToDouble", words); }
            if (category == WarpPortableStackCategory.Binary64) { return single ? [Service(typeof(WarpPortableNumericConversions), "DoubleToSingle", words)] : words; }
            string sourceType = (name is "conv.r.un" ? "UInt" : "Int") + (category == WarpPortableStackCategory.I4 ? "32" : "64");
            return single ? [Service(typeof(WarpPortableNumericConversions), sourceType + "ToSingle", words)] : WideService(typeof(WarpPortableNumericConversions), sourceType + "ToDouble", words);
        }

        private int[] FloatToInteger(string name, WarpPortableStackCategory category, int[] words, int offset)
        {
            if (name is "conv.i" or "conv.u") { name = name is "conv.u" ? "conv.u8" : "conv.i8"; }
            if (name is not ("conv.i4" or "conv.u4" or "conv.i8" or "conv.u8")) { throw Error("A narrow float conversion requires its exact-width portable conversion service.", offset); }
            string operation = (category == WarpPortableStackCategory.Binary32 ? "Single" : "Double") + "To" + (name[5] == 'u' ? "UInt" : "Int") + (name[6] == '8' ? "64" : "32");
            return name[6] == '8' ? WideService(typeof(WarpPortableNumericConversions), operation, words) : [Service(typeof(WarpPortableNumericConversions), operation, words)];
        }
    }
}
