using System.Collections.Immutable;

namespace WarpCLR.Verifier;

internal sealed record WarpPortableTypedSlot(
    WarpPortableTypedValue Value, ImmutableArray<bool> InitializedBytes);
