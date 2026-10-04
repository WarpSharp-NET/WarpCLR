namespace WarpCLR.Verifier;

internal sealed record WarpPortableTypedProvenance(
    WarpPortableProvenanceKind Kind, string OwnerMethod, int OwnerIndex,
    string OwnerType, int ByteOffset, int ByteLength);
