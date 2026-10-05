using System.Collections.Immutable;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed record WarpPortableWordResultRoot(int ResultWordOffset, bool IsInteriorOwner,
    ImmutableArray<WarpPortableTypedProvenance> Provenance);
