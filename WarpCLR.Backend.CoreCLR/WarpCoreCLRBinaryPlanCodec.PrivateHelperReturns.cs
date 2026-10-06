namespace WarpCLR.Backend.CoreCLR;

internal static partial class WarpCoreCLRBinaryPlanCodec
{
    // Opt-in profile only. Existing nonfenced plans retain their exact WBP6
    // encoding; WBP8 may not carry old metadata, or WBP6 new fenced metadata.
    internal const string ReturnFenceVersion = "warp.coreclr.binary-word-plan/private-helper-return-scoped-publication-fence/0.8";
    private const uint ReturnFenceMagic = 0x57425038;

    private static bool ReadWireProfile(BinaryReader reader)
    {
        uint magic = reader.ReadUInt32(); string version = ReadString(reader);
        if (magic == ReturnFenceMagic && string.Equals(version, ReturnFenceVersion, StringComparison.Ordinal)) { return true; }
        if (magic == Magic && string.Equals(version, Version, StringComparison.Ordinal)) { return false; }
        throw new InvalidDataException("The exact binary word-plan wire profile is unsupported.");
    }
}
