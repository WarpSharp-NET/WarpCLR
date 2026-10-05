using System.Collections.Immutable;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed record WarpPortableWordTransientRoot(int SsaWordOffset, bool IsInteriorOwner,
    ImmutableArray<WarpPortableTypedProvenance> Provenance);
