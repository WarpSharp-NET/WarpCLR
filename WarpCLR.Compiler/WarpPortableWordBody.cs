using System.Collections.Immutable;

namespace WarpCLR.Compiler;

internal sealed record WarpPortableWordBody(int Function, string MethodIdentity,
    ImmutableArray<WarpPortableWordStorageSlot> Arguments,
    ImmutableArray<WarpPortableWordStorageSlot> Locals, int EvaluationWordOffset,
    int MaximumStackWords, int PrivateWordCount, ImmutableArray<WarpPortableWordSourceBlock> SourceBlocks)
{
    internal int StoragePrefixWords => EvaluationWordOffset;
    internal int AliasOwnerFunction { get; init; } = -1;
    internal int AliasPrefixWords { get; init; }
    internal ImmutableArray<WarpPortableWordPrivateTemporary> PrivateTemporaries { get; init; } = [];
    internal WarpPortableWordInvocationPrelude? InvocationPrelude { get; init; }
}
