using System.Collections.Immutable;

namespace WarpCLR.Verifier;

internal sealed partial class WarpPortableTypedTypeCatalog
{
    private void ValidateOpaqueOverlaps(ImmutableArray<WarpPortableTypedField> members)
    {
        foreach (WarpPortableTypedField field in members.Where(field => !field.IsStatic && ContainsOpaqueToken(field.TypeIdentity)))
        {
            if (members.Any(other => !other.IsStatic && other != field && other.ByteOffset < field.ByteOffset + field.ByteSize &&
                other.ByteOffset + other.ByteSize > field.ByteOffset))
            {
                throw Error("An opaque runtime handle cannot overlap numeric/value fields and expose its private token representation.");
            }
        }
    }

    private bool ContainsOpaqueToken(string identity)
    {
        WarpPortableTypedType type = Get(identity);
        return type.Category is WarpPortableStackCategory.Handle or WarpPortableStackCategory.FunctionTarget ||
            type.Category == WarpPortableStackCategory.Value && type.Fields.Any(field => !field.IsStatic && ContainsOpaqueToken(field.TypeIdentity));
    }
}
