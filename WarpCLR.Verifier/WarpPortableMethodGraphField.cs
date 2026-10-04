using System.Collections.Immutable;
using System.Reflection;

namespace WarpCLR.Verifier;

internal sealed record WarpPortableMethodGraphField(
    int Id,
    string Identity,
    FieldInfo SourceField,
    string DeclaringType,
    string FieldType,
    bool IsStatic,
    bool IsReadOnly,
    bool IsLiteral,
    int? DeclaredOffset,
    string? LiteralBits,
    ImmutableArray<byte> InitializedData);
