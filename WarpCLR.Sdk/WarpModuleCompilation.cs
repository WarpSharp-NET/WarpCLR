using System.Collections.ObjectModel;
using WarpCLR.Compiler;
using WarpCLR.Verifier;

namespace WarpCLR.Sdk;

public sealed class WarpModuleCompilation
{
    internal WarpModuleCompilation(
        WarpVerifiedModule module,
        IDictionary<string, WarpCompilation> entries)
    {
        Module = module;
        Entries = new ReadOnlyDictionary<string, WarpCompilation>(
            new Dictionary<string, WarpCompilation>(entries, StringComparer.Ordinal));
    }

    public WarpVerifiedModule Module { get; }

    public IReadOnlyDictionary<string, WarpCompilation> Entries { get; }
}
