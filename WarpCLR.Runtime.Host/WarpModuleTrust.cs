using System.Collections.Frozen;
using System.Security.Cryptography;

namespace WarpCLR.Runtime.Host;

public sealed class WarpModuleTrust
{
    private readonly FrozenSet<string> assemblyHashes;

    public WarpModuleTrust(IEnumerable<string> trustedAssemblySha256)
    {
        ArgumentNullException.ThrowIfNull(trustedAssemblySha256);
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (string hash in trustedAssemblySha256)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(hash, nameof(trustedAssemblySha256));
            if (hash.Length != 64 || !hash.All(char.IsAsciiHexDigit))
            {
                throw new ArgumentException("A trusted assembly identity must be a SHA-256 hexadecimal digest.", nameof(trustedAssemblySha256));
            }

            hashes.Add(hash.ToUpperInvariant());
        }

        assemblyHashes = hashes.ToFrozenSet(StringComparer.Ordinal);
    }

    internal string Verify(ReadOnlySpan<byte> assembly)
    {
        string hash = Convert.ToHexString(SHA256.HashData(assembly));
        if (!assemblyHashes.Contains(hash))
        {
            throw new WarpHostException("WRPRUNTIME1000", "The assembly is not authorized by the caller's module trust policy.");
        }

        return hash;
    }
}
