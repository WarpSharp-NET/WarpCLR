using System.Collections.Immutable;

namespace WarpCLR.Verifier;

internal sealed record WarpPortableTypedValue(
    string TypeIdentity, WarpPortableStackCategory Category, int WordCount,
    ImmutableArray<WarpPortableTypedProvenance> Provenance,
    bool IsReadOnly = false, bool IsUninitializedThis = false, bool IsNull = false,
    string? MethodTarget = null, string? SourceStorageType = null);
