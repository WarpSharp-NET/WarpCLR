using System.Collections.Immutable;

namespace WarpCLR.Compiler;

internal sealed record WarpPortableExceptionSiteBinding(int SourceOffset, ImmutableArray<int> GeneratedBlocks);
