using System.Collections.Immutable;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed record WarpPortableWordSourceBlock(int Block, WarpPortableTypedInstruction Instruction,
    ImmutableArray<WarpPortableWordRoot> Roots, ImmutableArray<WarpPortableWordTransientRoot> ReturnedRoots,
    ImmutableArray<int> GeneratedBlocks, WarpPortableSourceOperationMetadata Operation);
