using System.Collections.Immutable;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed record WarpPortableClosedExceptionAccessor(string MethodIdentity, int SourceOffset, ushort SourceOpCode,
    string TargetIdentity, uint DeclaringType, WarpPortableExceptionAccessorKind Kind, string InstructionHash,
    bool VirtualCall, ImmutableArray<uint> ExactDataTargetTypes);

