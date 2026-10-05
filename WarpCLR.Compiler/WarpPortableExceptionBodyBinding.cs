using System.Collections.Immutable;

namespace WarpCLR.Compiler;

internal sealed record WarpPortableExceptionBodyBinding(string MethodIdentity, int Function,
    int PrivateWords, int EvaluationWordOffset, ImmutableArray<WarpPortableExceptionSiteBinding> Sites)
{
    internal int AliasOwnerFunction { get; init; } = -1;
    internal int AliasPrefixWords { get; init; }
    internal ImmutableArray<WarpPortableWordPrivateTemporary> PrivateTemporaries { get; init; } = [];
}
