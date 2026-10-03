using System.Collections.Frozen;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Runtime.Host;

public sealed class WarpRuntimeModule
{
    private const int MaximumAssemblyBytes = 64 * 1024 * 1024;

    private WarpRuntimeModule(WarpVerifiedModule module)
    {
        ManifestHash = module.ManifestHash;
        AssemblyHash = module.AssemblyHash;
        ProfileId = WarpProfileCatalog.ProfileId;
        Entries = module.Entries.ToFrozenDictionary(entry => entry.Identity, entry => new WarpRuntimeEntry(entry), StringComparer.Ordinal);
    }

    public string ManifestHash { get; }

    public string AssemblyHash { get; }

    public string ProfileId { get; }

    public IReadOnlyDictionary<string, WarpRuntimeEntry> Entries { get; }

    public static WarpRuntimeModule Load(ReadOnlyMemory<byte> assemblyBytes, WarpModuleTrust trust)
    {
        ArgumentNullException.ThrowIfNull(trust);
        if (assemblyBytes.IsEmpty || assemblyBytes.Length > MaximumAssemblyBytes)
        {
            throw new WarpHostException("WRPRUNTIME1001", "The assembly exceeds the module intake resource limit.");
        }

        // Verify and lower exactly the owned bytes authorized by the caller.
        byte[] snapshot = assemblyBytes.ToArray();
        _ = trust.Verify(snapshot);
        return new WarpRuntimeModule(new WarpModuleVerifier().Verify(snapshot));
    }
}
