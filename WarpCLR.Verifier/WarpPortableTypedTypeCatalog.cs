using System.Collections.Immutable;
using System.Reflection;
using WarpCLR.IR;

namespace WarpCLR.Verifier;

internal sealed partial class WarpPortableTypedTypeCatalog
{
    private readonly Dictionary<string, Type> sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WarpPortableMethodGraphType> captured;
    private readonly Dictionary<string, WarpPortableMethodGraphField> fields;
    private readonly Dictionary<string, WarpPortableTypedType> types = new(StringComparer.Ordinal);
    private readonly HashSet<string> constructing = new(StringComparer.Ordinal);
    private readonly WarpPortableCliSizeContract? cliSizes;

    public WarpPortableTypedTypeCatalog(WarpPortableMethodGraph graph, WarpPortableCliSizeContract? cliSizes = null)
    {
        this.cliSizes = cliSizes;
        cliSizes?.RequireGraph(graph);
        captured = graph.Types.ToDictionary(type => type.Identity, StringComparer.Ordinal);
        fields = graph.Fields.ToDictionary(field => field.Identity, StringComparer.Ordinal);
        foreach (WarpPortableMethodGraphType type in graph.Types)
        {
            sources.Add(type.Identity, type.SourceType);
        }

        Type[] builtins = [typeof(void), typeof(bool), typeof(byte), typeof(sbyte), typeof(char), typeof(short),
            typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double),
            typeof(object), typeof(string), typeof(Exception), typeof(RuntimeTypeHandle), typeof(RuntimeFieldHandle), typeof(RuntimeMethodHandle)];
        foreach (Type type in builtins)
        {
            sources.TryAdd(WarpPortableMethodGraphIdentity.Type(type), type);
        }

        types.Add("verified-method-target", new("verified-method-target", WarpPortableStackCategory.FunctionTarget,
            0, false, 4, 4, 1, 0, null, [], []));
        if (cliSizes is not null) { types.Add(WarpPortableCliNativeInteger.Identity, WarpPortableCliNativeInteger.Storage); }
        foreach (string identity in sources.Keys.ToArray())
        {
            Get(identity);
        }

        foreach (WarpPortableMethodGraphType type in graph.Types)
        {
            if (!type.SourceType.IsValueType && !type.SourceType.HasElementType && type.Fields.Length != 0)
            {
                BuildReferenceLayout(type.Identity);
            }
        }
    }

    public ImmutableArray<WarpPortableTypedType> Snapshot() => types.Values.OrderBy(type => type.Identity, StringComparer.Ordinal).ToImmutableArray();

    public WarpPortableTypedType Get(Type type)
    {
        string identity = WarpPortableMethodGraphIdentity.Type(type);
        sources.TryAdd(identity, type);
        return Get(identity);
    }

    public WarpPortableTypedType Get(string identity)
    {
        if (types.TryGetValue(identity, out WarpPortableTypedType? existing))
        {
            return existing;
        }

        Type source = Source(identity);
        if (!constructing.Add(identity))
        {
            throw Error("A value-type layout recursively contains itself.");
        }

        if (constructing.Count > 64) { throw Error("A value layout exceeds the bounded nesting depth of 64."); }
        WarpPortableTypedType result = BuildStorage(identity, source);
        types.Add(identity, result);
        constructing.Remove(identity);
        return result;
    }

    public Type Source(string identity) => sources.TryGetValue(identity, out Type? type) ? type :
        throw Error($"Typed metadata has no source type for '{identity}'.");

    public WarpPortableTypedField Field(string identity)
    {
        WarpPortableMethodGraphField field = fields[identity];
        WarpPortableTypedType owner = Get(field.DeclaringType);
        if (owner.Category == WarpPortableStackCategory.Reference && owner.Fields.IsEmpty)
        {
            BuildReferenceLayout(owner.Identity);
            owner = Get(owner.Identity);
        }

        return owner.Fields.First(candidate => string.Equals(candidate.Identity, identity, StringComparison.Ordinal));
    }

    public WarpPortableTypedValue Value(string identity) => new(identity, Get(identity).Category, Get(identity).WordCount, []);

    internal WarpPortableTypedValue CliNativeValue(int sourceOffset)
    {
        if (cliSizes?.NativeEvaluationBits != 64)
        {
            throw new WarpVerificationException("WRPCLR2210", "Native-integer CIL requires its exact graph/profile-bound CLI64 evaluation-width contract.", sourceOffset);
        }
        return Value(WarpPortableCliNativeInteger.Identity);
    }

    private static WarpVerificationException Error(string message) => new("WRPCLR2200", message, 0);
}
