using System.Collections.Immutable;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableSourceHeapSchema
{
    private sealed partial class Builder
    {
        private ImmutableArray<WarpPortableSourceHeapField> FieldMaps()
        {
            var maps = ImmutableArray.CreateBuilder<WarpPortableSourceHeapField>();
            foreach (WarpPortableMethodGraphField field in graph.Fields)
            {
                if (sources[field.DeclaringType].IsEnum) { continue; }
                WarpPortableTypedField layout = storage[field.DeclaringType].Fields.First(candidate => string.Equals(candidate.Identity, field.Identity, StringComparison.Ordinal));
                int prefix = field.IsStatic ? 0 : Prefix(sources[field.DeclaringType]);
                maps.Add(new(field.Identity, typeIds[field.DeclaringType], typeIds[field.FieldType], layout.ByteOffset,
                    checked(prefix + layout.ByteOffset), layout.ByteSize, field.IsStatic, field.IsReadOnly, field.IsLiteral));
            }
            return maps.OrderBy(map => map.Identity, StringComparer.Ordinal).ToImmutableArray();
        }

        private ImmutableArray<WarpPortableHeapReferenceLayout> References(IEnumerable<WarpPortableTypedField> fields, int prefix)
        {
            var roots = new SortedDictionary<uint, uint>();
            foreach (WarpPortableTypedField field in fields)
            {
                AddReference(roots, field.TypeIdentity, checked(prefix + field.ByteOffset));
            }
            return roots.Select(pair => new WarpPortableHeapReferenceLayout(pair.Key, pair.Value)).ToImmutableArray();
        }

        private void AddReference(SortedDictionary<uint, uint> roots, string identity, int byteOffset)
        {
            WarpPortableTypedType type = storage[identity];
            if (type.Category == WarpPortableStackCategory.Reference)
            {
                if (byteOffset % 4 != 0 || !roots.TryAdd((uint)byteOffset / 4, typeIds[identity]))
                {
                    throw new WarpVerificationException("WRPCLR2400", "A source owner reference is misaligned or duplicated in the heap layout.", 0);
                }
                return;
            }
            if (type.Category == WarpPortableStackCategory.ManagedByref)
            {
                throw new WarpVerificationException("WRPCLR2400", "A managed byref cannot become an escaping heap/static field.", 0);
            }
            foreach (WarpPortableTypedField field in type.Fields.Where(field => !field.IsStatic))
            {
                AddReference(roots, field.TypeIdentity, checked(byteOffset + field.ByteOffset));
            }
        }
    }
}
