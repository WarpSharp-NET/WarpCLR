using System.Collections.Immutable;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableWordLowerer
{
    private sealed partial class MethodLowerer
    {
        private WarpBlockTerminator? BindInstruction(WarpPortableMethodGraphInstruction input, WarpPortableTypedInstruction typed)
        {
            if (owner.Binding is null) { return null; }
            int originalValues = value, originalInstructions = instructions.Count, originalBlocks = blocks.Count;
            var context = new InstructionContext(this, input, typed, sourceBlocks[typed.Offset]);
            try
            {
                WarpBlockTerminator? terminator = owner.Binding.LowerInstruction(context);
                if (terminator is null && (value != originalValues || instructions.Count != originalInstructions || blocks.Count != originalBlocks))
                {
                    throw Error("An unbound instruction cannot retain partially emitted effects or continuations.", input.Offset);
                }
                return terminator;
            }
            finally { context.Close(); }
        }

        private sealed class InstructionContext(MethodLowerer emitter, WarpPortableMethodGraphInstruction input,
            WarpPortableTypedInstruction typed, int block) : WarpPortableWordInstructionContext
        {
            private bool active = true;
            internal override WarpPortableMethodGraph Graph { get { Check(); return emitter.owner.Graph; } }
            internal override WarpPortableTypedProgram Program { get { Check(); return emitter.owner.Program; } }
            internal override WarpPortableMethodGraphMethod SourceMethod { get { Check(); return emitter.source; } }
            internal override WarpPortableMethodGraphInstruction SourceInstruction { get { Check(); return input; } }
            internal override WarpPortableTypedInstruction Instruction { get { Check(); return typed; } }
            internal override WarpPortableWordBody Body { get { Check(); return emitter.Body; } }
            internal override int Block { get { Check(); return block; } }
            internal override WarpPortableGeneratedServiceImporter Services { get { Check(); return emitter.owner.RuntimeImporter; } }
            internal override int SourceBlock(int sourceOffset) { Check(); return emitter.sourceBlocks[sourceOffset]; }
            internal override int SourceFunction(string methodIdentity)
            {
                Check();
                if (!emitter.owner.PlannedBodies.Any(body => string.Equals(body.MethodIdentity, methodIdentity, StringComparison.Ordinal)))
                {
                    throw emitter.Error("The runtime binding needs an explicitly reserved captured source function.", typed.Offset);
                }
                return emitter.owner.FunctionIds[methodIdentity];
            }
            internal override int Constant(uint value) { Check(); return emitter.Constant(value); }
            internal override int Emit(WarpIrOpCode operation, int left = -1, int right = -1, uint immediate = 0, int third = -1)
            {
                Check(); return emitter.Emit(operation, left, right, immediate, third);
            }
            internal override int LoadPrivateWord(int offset) { CheckWord(offset); return emitter.Load(offset); }
            internal override void StorePrivateWord(int offset, int value) { CheckWord(offset); emitter.Store(offset, value); }
            internal override ImmutableArray<int> LoadStackValue(int slot)
            {
                Check(); ArgumentOutOfRangeException.ThrowIfNegative(slot); ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, typed.EntryStack.Length);
                return emitter.StackValue(typed, slot).ToImmutableArray();
            }
            internal override int StackWordOffset(int slot)
            {
                Check(); ArgumentOutOfRangeException.ThrowIfNegative(slot); ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, typed.EntryStack.Length);
                return emitter.StackWord(typed, slot);
            }
            internal override ImmutableArray<int> Call(int function, IEnumerable<int> arguments, int resultWords = 1)
            {
                Check(); ArgumentNullException.ThrowIfNull(arguments); ArgumentOutOfRangeException.ThrowIfNegative(resultWords);
                WarpCompilationAdmission.Require(emitter.method.Identity, WarpCompilationResourceKind.ValueSlots, resultWords, WarpCompilationAdmission.MaximumValueSlotsPerEntry);
                int result = resultWords == 0 ? -1 : emitter.value; emitter.value = checked(emitter.value + resultWords);
                emitter.instructions.Add(new(result, function, arguments, resultWords));
                return Enumerable.Range(Math.Max(0, result), resultWords).ToImmutableArray();
            }
            internal override int ReserveGeneratedBlock() { Check(); return emitter.ExtraBlock(typed.Offset); }
            internal override void EmitGeneratedBlock(int generated, Func<WarpPortableWordInstructionContext, WarpBlockTerminator> implementation)
            {
                Check(); ArgumentNullException.ThrowIfNull(implementation);
                if (!emitter.generatedBlocks.GetValueOrDefault(typed.Offset, []).Contains(generated) || emitter.blocks[generated] is not null)
                {
                    throw emitter.Error("The generated block is not a fresh reservation for this original source instruction.", typed.Offset);
                }
                List<WarpIrInstruction> previous = emitter.instructions; emitter.instructions = [];
                var context = new InstructionContext(emitter, input, typed, generated);
                try { emitter.blocks[generated] = new(generated, [], emitter.instructions, implementation(context)); }
                finally { context.Close(); emitter.instructions = previous; }
            }
            internal override void RecordReturnedRoots(int resultWord, string typeIdentity)
            {
                Check(); ArgumentOutOfRangeException.ThrowIfNegative(resultWord);
                emitter.RecordReturnedRoots(typed, resultWord, emitter.owner.Types[typeIdentity]);
            }
            internal override void RequireService(string identity)
            {
                Check(); ArgumentException.ThrowIfNullOrWhiteSpace(identity); emitter.owner.Services.Add(identity);
            }
            internal void Close() => active = false;
            private void Check()
            {
                if (!active) { throw new InvalidOperationException("The source instruction emission capability is closed."); }
                emitter.owner.RequireBindingMutation();
            }
            private void CheckWord(int offset)
            {
                Check(); ArgumentOutOfRangeException.ThrowIfNegative(offset); ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(offset, emitter.Body.PrivateWordCount);
            }
        }
    }
}
