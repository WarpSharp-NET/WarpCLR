using System.Collections.Immutable;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal abstract class WarpPortableWordInstructionContext
{
    internal abstract WarpPortableMethodGraph Graph { get; }
    internal abstract WarpPortableTypedProgram Program { get; }
    internal abstract WarpPortableMethodGraphMethod SourceMethod { get; }
    internal abstract WarpPortableMethodGraphInstruction SourceInstruction { get; }
    internal abstract WarpPortableTypedInstruction Instruction { get; }
    internal abstract WarpPortableWordBody Body { get; }
    internal abstract WarpPortableGeneratedServiceImporter Services { get; }
    internal abstract int Block { get; }
    internal int Function => Body.Function - 1;
    internal int SourceEntryBlock => Body.SourceBlocks.First(block => block.Instruction.Offset == Instruction.Offset).Block;
    internal abstract int SourceBlock(int sourceOffset);
    internal abstract int SourceFunction(string methodIdentity);
    internal abstract int Constant(uint value);
    internal abstract int Emit(WarpIrOpCode operation, int left = -1, int right = -1, uint immediate = 0, int third = -1);
    internal abstract int LoadPrivateWord(int offset);
    internal abstract void StorePrivateWord(int offset, int value);
    internal abstract ImmutableArray<int> LoadStackValue(int slot);
    internal abstract int StackWordOffset(int slot);
    internal abstract ImmutableArray<int> Call(int function, IEnumerable<int> arguments, int resultWords = 1);
    internal abstract int ReserveGeneratedBlock();
    internal abstract void EmitGeneratedBlock(int block, Func<WarpPortableWordInstructionContext, WarpBlockTerminator> emitter);
    internal abstract void RecordReturnedRoots(int resultWord, string typeIdentity);
    internal abstract void RequireService(string identity);

    internal WarpBranchTerminator Next() => Instruction.Successors.Length == 1 ? new(new(SourceBlock(Instruction.Successors[0]), [])) :
        throw new WarpVerificationException("WRPCLR2300", "This source operation needs an explicit generated control continuation.", Instruction.Offset);
}
