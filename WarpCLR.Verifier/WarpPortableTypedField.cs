namespace WarpCLR.Verifier;

internal sealed record WarpPortableTypedField(
    string Identity, string TypeIdentity, int ByteOffset, int ByteSize,
    bool IsStatic, bool IsReadOnly);
