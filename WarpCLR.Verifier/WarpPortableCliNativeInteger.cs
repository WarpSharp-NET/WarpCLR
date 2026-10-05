namespace WarpCLR.Verifier;

// This is a CLI evaluation category. It never admits a source native-address storage type.
internal static class WarpPortableCliNativeInteger
{
    internal const string Semantics = "warp.cli-native-integer/coreclr64-evaluation-pair-opcode-specific-i4-promotion-explicit-storage-coercion/0.1";
    internal const string Identity = "warp.cli-evaluation/native-integer64/0.1";
    internal static WarpPortableTypedType Storage => new(Identity, WarpPortableStackCategory.CliNativeInteger,
        64, false, 8, 8, 2, 0, null, [], []);
}
