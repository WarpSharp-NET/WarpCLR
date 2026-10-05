using System.Text;

namespace WarpCLR.Verifier;

// Manifest files are UTF8 text. Invalid raw UTF16 input must be rejected before
// encoding; an encoder replacement is not an alternative manifest identity.
internal static class WarpManifestUtf8Text
{
    internal const string Semantics = "warp.manifest-text/strict-utf8-no-unpaired-utf16-replacement/0.1";
    private static readonly UTF8Encoding Encoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal static byte[] Bytes(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try { return Encoding.GetBytes(text); }
        catch (EncoderFallbackException) { throw Invalid(); }
    }

    internal static int ByteCount(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try { return Encoding.GetByteCount(text); }
        catch (EncoderFallbackException) { throw Invalid(); }
    }

    private static WarpVerificationException Invalid() => new("WRPMAN1001",
        "A manifest contains an unpaired raw UTF16 surrogate and cannot be encoded as exact UTF8 text.");
}
