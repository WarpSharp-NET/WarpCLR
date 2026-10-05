using System.Collections.Immutable;

namespace WarpCLR.Verifier;

// A typed program is still internal: backend/service support remains a separate gate.
internal sealed partial class WarpPortableTypedProgram
{
    public const string Version = "warp.portable-typed-cil/controlled-mutability-nullable-owned-copy-cli64-evaluation/0.3";

    private WarpPortableTypedProgram(string graphHash, string verifiedHash,
        ImmutableArray<WarpPortableTypedType> types, ImmutableArray<WarpPortableTypedMethod> methods,
        WarpPortableCliSizeContract? cliSizes)
    {
        GraphHash = graphHash;
        VerifiedHash = verifiedHash;
        Types = types;
        Methods = methods;
        CliSizes = cliSizes;
    }

    public string GraphHash { get; }
    public string VerifiedHash { get; }
    public ImmutableArray<WarpPortableTypedType> Types { get; }
    public ImmutableArray<WarpPortableTypedMethod> Methods { get; }
    internal WarpPortableCliSizeContract? CliSizes { get; }

    public static WarpPortableTypedProgram Verify(WarpPortableMethodGraph graph, WarpPortableCliSizeContract? cliSizes = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        cliSizes?.RequireGraph(graph);
        return new Builder(graph, cliSizes).Verify();
    }
}
