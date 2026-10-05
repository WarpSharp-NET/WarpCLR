using System.Collections.Frozen;
using WarpCLR.Compiler;
using WarpCLR.Verifier;

namespace WarpCLR.Runtime.Host;

internal sealed partial class WarpCompiledSourcePlan
{
    internal bool RequiresSourceLoan(uint[] state)
    {
        WarpCompiledSourceLocation location = Location(state);
        return location.Block is { } block && sourceLoans[(location.Function, block.Block)];
    }

    private static FrozenDictionary<(int Function, int Block), bool> SourceLoanMap(WarpPortableWordLoweredProgram program)
    {
        FrozenDictionary<string, WarpPortableTypedType> types = program.VerifiedProgram.Types.ToFrozenDictionary(type => type.Identity, StringComparer.Ordinal);
        return program.Bodies.SelectMany(body => body.SourceBlocks.Select(block =>
            (Key: (body.Function, block.Block), Value: NeedsLoan(block.Instruction, types)))).ToFrozenDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static bool NeedsLoan(WarpPortableTypedInstruction instruction, FrozenDictionary<string, WarpPortableTypedType> types)
    {
        if (instruction.Effects.Any(effect => effect is WarpPortableTypedEffect.ReadMemory or WarpPortableTypedEffect.WriteMemory or
            WarpPortableTypedEffect.Allocate or WarpPortableTypedEffect.TypeInitialize)) { return true; }
        return instruction.Effects.Contains(WarpPortableTypedEffect.PrivateWrite) && instruction.MemoryType is { } memory &&
            !types[memory].ManagedRootByteOffsets.IsEmpty;
    }
}
