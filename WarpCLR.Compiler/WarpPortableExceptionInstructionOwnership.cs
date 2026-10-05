using System.Collections.Immutable;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

// Compile-time ownership of exact captured instructions. The throw predicate
// is a required privately validated invocation domain, not an authority token.
internal sealed record WarpPortableExceptionInstructionOwnership(string MethodIdentity, int SourceOffset,
    ushort SourceOpCode, ImmutableArray<WarpPortableTypedEffect> Effects,
    ImmutableArray<WarpPortableTypedExceptionMembership> ExceptionMemberships, string InstructionHash,
    WarpPortableTypedValue? ThrowOperand, bool RequiresNonNullPreparedException);
