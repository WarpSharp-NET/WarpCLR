using System.Collections.Immutable;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal static partial class WarpPortableWordLowerer
{
    private sealed partial class MethodLowerer
    {
        private readonly Builder owner;
        private readonly WarpPortableMethodGraphMethod source;
        private readonly WarpPortableTypedMethod method;
        private readonly int function;
        private readonly ImmutableArray<WarpPortableWordStorageSlot> arguments;
        private readonly ImmutableArray<WarpPortableWordStorageSlot> locals;
        private readonly Dictionary<int, int> sourceBlocks;
        private readonly List<WarpBasicBlock?> blocks;
        private readonly List<int> charges;
        private readonly int stackOffset;
        private readonly Dictionary<int, ImmutableArray<WarpPortableWordTransientRoot>> returnedRoots = [];
        private readonly Dictionary<int, List<int>> generatedBlocks = [];
        private int value;
        private List<WarpIrInstruction> instructions = [];

        internal MethodLowerer(Builder owner, WarpPortableMethodGraphMethod source, WarpPortableTypedMethod method, int function)
        {
            this.owner = owner; this.source = source; this.method = method; this.function = function;
            int word = 0; arguments = Slots(method.ArgumentTypes, ref word); locals = Slots(method.LocalTypes, ref word);
            stackOffset = word;
            PrepareUnsignedSingleConversions();
            sourceBlocks = method.Instructions.Where(instruction => instruction.Reachable).Select((instruction, index) => (instruction.Offset, Block: index + 1))
                .ToDictionary(pair => pair.Offset, pair => pair.Block);
            blocks = Enumerable.Repeat<WarpBasicBlock?>(null, sourceBlocks.Count + 1).ToList();
            charges = [0, .. Enumerable.Repeat(1, sourceBlocks.Count)];
            Body = new(function + 1, method.Identity, arguments, locals, stackOffset, method.MaximumStackWords,
                checked(stackOffset + method.MaximumStackWords + (directUnsignedSingles.Count == 0 ? 0 : 2)), method.Instructions.Where(instruction => instruction.Reachable)
                    .Select(instruction => new WarpPortableWordSourceBlock(sourceBlocks[instruction.Offset], instruction, ProjectRoots(instruction), [], [sourceBlocks[instruction.Offset]], SourceOperation(instruction))).ToImmutableArray());
            Metadata = new(Body.PrivateWordCount, false, charges);
        }

        internal WarpPortableWordBody Body { get; private set; }
        internal WarpLogicalBodyMetadata Metadata { get; private set; }

        internal WarpControlFlowFunction Lower()
        {
            if (!source.ExceptionRegions.IsEmpty && owner.Binding?.ExceptionMethods.Contains(method.Identity, StringComparer.Ordinal) != true)
            {
                throw Error("Exception execution must be bound to generated EH continuations.", source.ExceptionRegions[0].TryOffset);
            }
            InitializeArguments();
            foreach (WarpPortableTypedInstruction typed in method.Instructions.Where(instruction => instruction.Reachable))
            {
                instructions = [];
                WarpPortableMethodGraphInstruction input = source.Instructions.First(instruction => instruction.Offset == typed.Offset);
                WarpBlockTerminator terminator = BindInstruction(input, typed) ?? LowerInstruction(input, typed);
                blocks[sourceBlocks[typed.Offset]] = new(sourceBlocks[typed.Offset], [], instructions, terminator);
            }
            Metadata = new(Body.PrivateWordCount, false, charges);
            Body = Body with { SourceBlocks = Body.SourceBlocks.Select(block => block with
                {
                    ReturnedRoots = returnedRoots.GetValueOrDefault(block.Instruction.Offset, []),
                    GeneratedBlocks = [block.Block, .. generatedBlocks.GetValueOrDefault(block.Instruction.Offset, [])],
                }).ToImmutableArray() };
            return new(function, method.Identity, arguments.Sum(slot => slot.Type.WordCount), blocks.Select(block => block ?? throw new InvalidOperationException("A source block is missing.")));
        }

        private ImmutableArray<WarpPortableWordStorageSlot> Slots(ImmutableArray<string> identities, ref int word)
        {
            var slots = ImmutableArray.CreateBuilder<WarpPortableWordStorageSlot>(identities.Length);
            for (int index = 0; index < identities.Length; index++)
            {
                WarpPortableTypedType type = owner.Types[identities[index]];
                slots.Add(new(index, word, type)); word = checked(word + type.WordCount);
            }
            return slots.MoveToImmutable();
        }

        private WarpVerificationException Error(string message, int offset) => Builder.Error(method.Identity, message, offset);
    }
}
