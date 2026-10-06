using System.Text;

namespace WarpCLR.Tests;

internal static class ManifestAssemblyFixture
{
    public const string MapEntryIdentity = "WarpCLR.Tests.TestKernels.ManifestMap";

    public const string ReductionEntryIdentity = "WarpCLR.Tests.TestKernels.ManifestReduction";

    public const string MapGraphHash =
        "4E601425355598D0DEFAA672543B371B83B13FB2B125CA7433006A1A33F84FE7";

    public const string ReductionGraphHash =
        "32DAA260F58DE8F2A529ECB7F53D8FBA2A81C6FFF5DF343CDB222AAB0F68D3FB";

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
            throw new ArgumentException("Replacement values must have the same UTF-8 length.", nameof(newValue));
        }

        int offset = source.AsSpan().IndexOf(oldBytes);
        if (offset < 0)
        {
            throw new ArgumentException("The source does not contain the requested UTF-8 value.", nameof(oldValue));
        }

        if (source.AsSpan(offset + oldBytes.Length).IndexOf(oldBytes) >= 0)
        {
            throw new ArgumentException("The source contains the requested UTF-8 value more than once.", nameof(oldValue));
        }

        byte[] result = source.ToArray();
        newBytes.CopyTo(result.AsSpan(offset));
        return result;
    }
}
