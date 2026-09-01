using System.Text;

namespace WarpCLR.Tests;

internal static class ManifestAssemblyFixture
{
    public static byte[] ReadAssembly() => File.ReadAllBytes(typeof(TestKernels).Assembly.Location);

    public static byte[] ReplaceUtf8(byte[] source, string oldValue, string newValue)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(oldValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(newValue);

        byte[] oldBytes = Encoding.UTF8.GetBytes(oldValue);
        byte[] newBytes = Encoding.UTF8.GetBytes(newValue);
        if (oldBytes.Length != newBytes.Length)
        {
            throw new ArgumentException("Replacement values must have the same UTF-8 length.");
        }

        int offset = source.AsSpan().IndexOf(oldBytes);
        if (offset < 0)
        {
            throw new ArgumentException("The source does not contain the requested UTF-8 value.");
        }

        if (source.AsSpan(offset + oldBytes.Length).IndexOf(oldBytes) >= 0)
        {
            throw new ArgumentException("The source contains the requested UTF-8 value more than once.");
        }

        byte[] result = source.ToArray();
        newBytes.CopyTo(result.AsSpan(offset));
        return result;
    }
}
