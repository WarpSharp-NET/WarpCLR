using System.Collections.Immutable;

namespace WarpCLR.Verifier;

internal sealed record WarpPortableCliTypeSize(string Identity, uint ByteSize, bool IsValueType,
    ImmutableArray<WarpPortableCliFieldSize> Fields);
