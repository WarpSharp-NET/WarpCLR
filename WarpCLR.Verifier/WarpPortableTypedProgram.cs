using System.Collections.Immutable;

namespace WarpCLR.Verifier;

// A typed program is still internal: backend/service support remains a separate gate.
internal sealed partial class WarpPortableTypedProgram
{
    public const string Version = "warp.portable-typed-cil/0.1";

    private WarpPortableTypedProgram(string graphHash, string verifiedHash,
        ImmutableArray<WarpPortableTypedType> types, ImmutableArray<WarpPortableTypedMethod> methods)
    {
        GraphHash = graphHash;
        VerifiedHash = verifiedHash;
        Types = types;
        Methods = methods;
    }

    public string GraphHash { get; }
    public string VerifiedHash { get; }
    public ImmutableArray<WarpPortableTypedType> Types { get; }
    public ImmutableArray<WarpPortableTypedMethod> Methods { get; }

    public static WarpPortableTypedProgram Verify(WarpPortableMethodGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return new Builder(graph).Verify();
    }
}
