using System.Collections.Immutable;

namespace WarpCLR.Compiler;

internal sealed record WarpPortableSourceFrameBody(uint Function, uint PrivateWords,
    ImmutableArray<WarpPortableSourceFrameView> Views);
