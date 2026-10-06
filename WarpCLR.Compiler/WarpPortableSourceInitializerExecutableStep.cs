using System.Collections.Immutable;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed record WarpPortableSourceInitializerExecutableStep(string MethodIdentity, int CapturedMethodIndex,
    int Function, int OriginalFunction, int EntryProgramCounter, WarpPortableWordSourceBlock Source,
    ImmutableArray<WarpPortableSourceInitializerExecutablePoint> Points)
{
    internal WarpPortableTypedInstruction Instruction => Source.Instruction;
    internal int SourceOffset => Instruction.Offset;
    internal ushort SourceOpCode => unchecked((ushort)Instruction.OpCode);
    internal bool Alias => OriginalFunction != Function;
}
