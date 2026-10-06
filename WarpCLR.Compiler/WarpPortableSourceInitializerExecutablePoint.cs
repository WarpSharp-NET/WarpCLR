using System.Collections.Immutable;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

// The copied block cost and the cost actually charged on this node are distinct.
internal sealed record WarpPortableSourceInitializerExecutablePoint(int ProgramCounter, int Function, int Block,
    bool StartsBlock, int SourceCost, int ChargedSourceSteps, bool RuntimeHelper, int AliasOwnerFunction,
    string? MethodIdentity, int? SourceOffset, ushort? SourceOpCode, bool InvocationPrelude,
    ImmutableArray<WarpPortableTypedExceptionMembership> ExceptionMemberships, string NodeHash);
