using System.Collections.Immutable;

namespace WarpCLR.Compiler;

internal sealed record WarpPortableSourceSegmentStorage(int PrivateWordCount, int EvaluationWordOffset,
    int AliasOwnerFunction, int AliasPrefixWords, ImmutableArray<WarpPortableWordStorageSlot> Arguments,
    ImmutableArray<WarpPortableWordStorageSlot> Locals, ImmutableArray<WarpPortableWordPrivateTemporary> Temporaries,
    ImmutableArray<WarpPortableSourceOperand> EntryOperands, ImmutableArray<WarpPortableSourceOperand> ExitOperands);
