using System.Text;

namespace WarpCLR.Tests;

internal static class ManifestAssemblyFixture
{
    public const string MapEntryIdentity = "WarpCLR.Tests.TestKernels.ManifestMap";

    public const string ReductionEntryIdentity = "WarpCLR.Tests.TestKernels.ManifestReduction";

    public const string MapGraphHash =
        "73EA6961D7383318BED4980E9B8EC8489C4322FC765E251A05001A5C08B9FDA0";

    public const string ReductionGraphHash =
        "A84B49A435675B1CE2FC775B9E5495C50E436AF4AFBDB507D3521A4582873CF2";

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
