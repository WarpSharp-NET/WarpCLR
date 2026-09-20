using System.Text;

namespace WarpCLR.Tests;

internal static class ManifestAssemblyFixture
{
    public const string MapEntryIdentity = "WarpCLR.Tests.TestKernels.ManifestMap";

    public const string ReductionEntryIdentity = "WarpCLR.Tests.TestKernels.ManifestReduction";

    public const string MapGraphHash =
        "9EE270B20F8DDB67E0134A1821813152B45B22261C303F5E8EA94D03D7385658";

    public const string ReductionGraphHash =
        "350CCE4D1001CECC70247AE0367F4E5711499C21BB83B84D39FE2097A8947CC4";

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
