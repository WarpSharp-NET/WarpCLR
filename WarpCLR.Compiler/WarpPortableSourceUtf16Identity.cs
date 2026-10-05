using WarpCLR.Verifier;
using System.Security.Cryptography;
using WarpCLR.IR;

namespace WarpCLR.Compiler;

// Source strings are sequences of UTF16 code units. JSON/UTF8 string encoders
// may replace an unpaired surrogate; hash numeric code units before encoding.
internal static class WarpPortableSourceUtf16Identity
{
    internal const string Semantics = "warp.source-text-identity/raw-utf16-code-unit-arrays-no-unicode-replacement-raw-utf16-snapshot/0.2";

    internal static uint[] CodeUnits(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        WarpCompilationAdmission.Require(Semantics, WarpCompilationResourceKind.VerifierWorkspaceSlots,
            text.Length, WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry);
        return text.Select(character => (uint)character).ToArray();
    }

    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(
        WarpPortableSnapshotIdentity.Serialize(new { Semantics, CodeUnits = CodeUnits(text) })));
}
