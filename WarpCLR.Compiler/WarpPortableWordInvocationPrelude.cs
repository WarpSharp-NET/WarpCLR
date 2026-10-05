using System.Collections.Immutable;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed record WarpPortableWordInvocationPrelude(string MethodIdentity, WarpPortableTypedInitializerTrigger Trigger,
    uint DeclaringType, string InitializerPlanHash, ImmutableArray<WarpPortableWordRoot> Roots, ImmutableArray<int> GeneratedBlocks)
{
    internal const string Semantics = "warp.source-invocation-prelude/original-frame-initialized-private-arguments-root-trigger-no-fabricated-il-zero-source-charge/0.1";
}
