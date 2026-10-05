using System.Collections.Immutable;

namespace WarpCLR.Verifier;

// A typed program is still internal: backend/service support remains a separate gate.
internal sealed partial class WarpPortableTypedProgram
{
    public const string Version = "warp.portable-typed-cil/exact-captured-type-initializer-triggers-cli64-raw-utf16-snapshot/0.5";

    private WarpPortableTypedProgram(string graphHash, string verifiedHash,
        ImmutableArray<WarpPortableTypedType> types, ImmutableArray<WarpPortableTypedMethod> methods,
        WarpPortableCliSizeContract? cliSizes, WarpPortableTypedInitializerTrigger? entryInitializerTrigger)
    {
        GraphHash = graphHash;
        VerifiedHash = verifiedHash;
        Types = types;
        Methods = methods;
        CliSizes = cliSizes;
        EntryInitializerTrigger = entryInitializerTrigger;
    }

    public string GraphHash { get; }
    public string VerifiedHash { get; }
    public ImmutableArray<WarpPortableTypedType> Types { get; }
    public ImmutableArray<WarpPortableTypedMethod> Methods { get; }
    internal WarpPortableCliSizeContract? CliSizes { get; }
    public WarpPortableTypedInitializerTrigger? EntryInitializerTrigger { get; }

    public static WarpPortableTypedProgram Verify(WarpPortableMethodGraph graph, WarpPortableCliSizeContract? cliSizes = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        cliSizes?.RequireGraph(graph);
        return new Builder(graph, cliSizes).Verify();
    }
}
