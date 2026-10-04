using System.Collections.Immutable;
using System.Reflection;

namespace WarpCLR.Verifier;

internal sealed record WarpPortableMethodGraphType(
    int Id,
    string Identity,
    Type SourceType,
    string? BaseType,
    ImmutableArray<string> Interfaces,
    ImmutableArray<string> Fields,
    string? Initializer,
    bool Instantiated,
    int LayoutKind,
    int PackingSize,
    int DeclaredSize,
    string? ElementType,
    int ArrayRank,
    string? EnumUnderlyingType);
