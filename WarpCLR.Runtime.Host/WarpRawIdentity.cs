namespace WarpCLR.Runtime.Host;

internal static class WarpRawIdentity
{
    internal static void WriteString(BinaryWriter writer, string value)
    {
        writer.Write(value.Length);
        foreach (char unit in value) { writer.Write((ushort)unit); }
    }
}
