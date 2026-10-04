using System.Collections.Immutable;

namespace WarpCLR.Verifier;

internal sealed record WarpPortableTypedReturnSummary(ImmutableArray<WarpPortableTypedProvenance> Origins, bool ReadOnly);
