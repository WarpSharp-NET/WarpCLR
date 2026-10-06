using System.Collections.Immutable;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

// Before-body invocation is distinct from an original CIL instruction. These
// fields cannot issue an initializer/factory ticket or a runtime grant.
internal sealed record WarpPortableSourceInvocation(string MethodIdentity, int CapturedMethodIndex,
    int Function, int EntryProgramCounter, string CapturedSignatureHash,
    ImmutableArray<int> LocalProgramCounters, ImmutableArray<int> HelperFunctions,
    ImmutableArray<WarpPortableSourceSegmentFrontier> Frontiers,
    ImmutableArray<WarpPortableWordStorageSlot> Arguments, WarpPortableTypedType ResultType,
    ImmutableArray<WarpPortableWordRoot> BeforeBodyRoots, WarpPortableWordEntryProjection EntryProjection,
    WarpPortableWordInvocationPrelude? Prelude)
{
    internal const string Version = "warp.source-invocation-segment/compiler-sealed-before-body-signature-private-arguments-no-fabricated-cil-or-target-eh/0.1";
    internal int OriginKind { get; } = 2;
    internal int? SourceOffset { get; }
    internal ushort? SourceOpCode { get; }
    internal int? EffectIndex { get; }
    internal ImmutableArray<WarpPortableTypedEffect> Effects { get; } = [];
    internal ImmutableArray<WarpPortableTypedExceptionMembership> ExceptionMemberships { get; } = [];
}
