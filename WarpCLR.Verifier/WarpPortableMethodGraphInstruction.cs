using System.Collections.Immutable;
using System.Reflection.Emit;

namespace WarpCLR.Verifier;

internal sealed record WarpPortableMethodGraphInstruction(
    int Offset,
    int NextOffset,
    OpCode OpCode,
    ulong Operand,
    ImmutableArray<int> BranchTargets,
    string? Method,
    string? Type,
    string? Field,
    string? StringLiteral);
