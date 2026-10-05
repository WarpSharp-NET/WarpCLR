using System.Collections.Immutable;
using System.Globalization;
using System.Resources;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

// Capture-time BCL resource data only. Generated execution consumes immutable UTF16
// data; it never calls a host resource manager, exception constructor or formatter.
internal sealed class WarpPortableSourceExceptionResources
{
    internal const string Semantics = "warp.source-exception-resources/captured-corelib-ui-culture-raw-utf16-content-runtime-profile-raw-utf16-snapshot/0.3";
    private const string ResourceBase = "System.Private.CoreLib.Strings";

    private WarpPortableSourceExceptionResources(string corelibHash, string uiCulture,
        ImmutableArray<WarpPortableSourceExceptionResource> resources)
    {
        CorelibHash = corelibHash; UiCulture = uiCulture; Resources = resources;
        RuntimeProfile = RuntimeInformation.FrameworkDescription;
        ContractHash = Convert.ToHexString(SHA256.HashData(WarpPortableSnapshotIdentity.Serialize(new
        {
            Semantics, CorelibHash,
            UiCulture = WarpPortableSourceUtf16Identity.CodeUnits(UiCulture),
            RuntimeProfile = WarpPortableSourceUtf16Identity.CodeUnits(RuntimeProfile),
            Text = WarpPortableSourceUtf16Identity.Semantics,
            Resources = resources.Select(resource => new
            {
                Key = WarpPortableSourceUtf16Identity.CodeUnits(resource.Key),
                Text = WarpPortableSourceUtf16Identity.CodeUnits(resource.Text),
            }),
        })));
    }

    internal string CorelibHash { get; }
    internal string UiCulture { get; }
    internal string RuntimeProfile { get; }
    internal string ContractHash { get; }
    internal ImmutableArray<WarpPortableSourceExceptionResource> Resources { get; }

    internal static WarpPortableSourceExceptionResources Capture(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        CultureInfo current = CultureInfo.CurrentUICulture;
        if (current.GetType() != typeof(CultureInfo))
        {
            throw new WarpVerificationException("WRPCLR2420", "An exception resource profile requires an exact standard captured UI culture.", 0);
        }
        string[] captured = WarpCompilationAdmission.Materialize(keys, ResourceBase, WarpCompilationResourceKind.ValueSlots, WarpCompilationAdmission.MaximumValueSlotsPerEntry);
        string[] names = captured.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        WarpCompilationAdmission.Require(ResourceBase, WarpCompilationResourceKind.ValueSlots, names.Length, WarpCompilationAdmission.MaximumValueSlotsPerEntry);
        var manager = new ResourceManager(ResourceBase, typeof(object).Assembly);
        try
        {
            var values = ImmutableArray.CreateBuilder<WarpPortableSourceExceptionResource>(names.Length);
            long units = 0;
            foreach (string key in names)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(keys));
                string text = manager.GetString(key, current) ?? throw new WarpVerificationException("WRPCLR2420", "An exception resource is absent from the exact captured Corelib profile: " + key, 0);
                units = checked(units + text.Length);
                WarpCompilationAdmission.Require(ResourceBase, WarpCompilationResourceKind.VerifierWorkspaceSlots, units, WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry);
                values.Add(new(key, text));
            }
            using FileStream image = File.OpenRead(typeof(object).Assembly.Location);
            return new(Convert.ToHexString(SHA256.HashData(image)), current.Name, values.MoveToImmutable());
        }
        finally { manager.ReleaseAllResources(); }
    }

    internal string Text(string key) => Resources.FirstOrDefault(resource => string.Equals(resource.Key, key, StringComparison.Ordinal))?.Text ??
        throw new WarpVerificationException("WRPCLR2420", "A fault factory requested uncaptured resource data: " + key, 0);
}
