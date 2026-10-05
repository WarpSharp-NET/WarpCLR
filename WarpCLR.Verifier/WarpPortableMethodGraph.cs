using System.Collections.Immutable;
using System.Reflection;

namespace WarpCLR.Verifier;

// Discovery produces a closure snapshot, not execution authority. The typed CIL
// verifier and runtime must implement every required intrinsic before admitting it.
internal sealed partial class WarpPortableMethodGraph
{
    public const string Version = "warp.portable-method-type-closure/exact-exception-accessors-cli64-raw-utf16-snapshot/0.6";

    private WarpPortableMethodGraph(string entryIdentity, string graphHash,
        ImmutableArray<WarpPortableMethodGraphMethod> methods,
        ImmutableArray<WarpPortableMethodGraphType> types,
        ImmutableArray<WarpPortableMethodGraphField> fields,
        ImmutableArray<WarpPortableMethodGraphDispatch> dispatches,
        ImmutableArray<string> intrinsics)
    {
        EntryIdentity = entryIdentity;
        GraphHash = graphHash;
        Methods = methods;
        Types = types;
        Fields = fields;
        Dispatches = dispatches;
        Intrinsics = intrinsics;
    }

    public string EntryIdentity { get; }

    public string GraphHash { get; }

    public ImmutableArray<WarpPortableMethodGraphMethod> Methods { get; }

    public ImmutableArray<WarpPortableMethodGraphType> Types { get; }

    public ImmutableArray<WarpPortableMethodGraphField> Fields { get; }

    public ImmutableArray<WarpPortableMethodGraphDispatch> Dispatches { get; }

    public ImmutableArray<string> Intrinsics { get; }

    public static WarpPortableMethodGraph Discover(MethodInfo entry,
        IEnumerable<Assembly>? permittedAssemblies = null, IEnumerable<Type>? concreteTypes = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new Builder(entry, permittedAssemblies, concreteTypes).Discover();
    }
}
