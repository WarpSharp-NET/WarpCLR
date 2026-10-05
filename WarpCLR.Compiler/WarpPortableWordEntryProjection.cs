using System.Collections.Immutable;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed record WarpPortableWordEntryProjection(WarpPortableTypedType ResultType,
    ImmutableArray<WarpPortableWordTransientRoot> WrapperInputRoots,
    ImmutableArray<WarpPortableWordTransientRoot> WrapperReturnedRoots,
    ImmutableArray<WarpPortableWordResultRoot> ResultRoots);
