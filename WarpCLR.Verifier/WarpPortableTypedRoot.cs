using System.Collections.Immutable;

namespace WarpCLR.Verifier;

internal sealed record WarpPortableTypedRoot(
    string Storage, int Slot, int WordOffset, bool IsInteriorOwner,
    ImmutableArray<WarpPortableTypedProvenance> Provenance);
