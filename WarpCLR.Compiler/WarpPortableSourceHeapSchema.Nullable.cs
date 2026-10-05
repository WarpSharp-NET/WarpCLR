using System.Collections.Immutable;
using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableSourceHeapSchema
{
    private sealed partial class Builder
    {
        private ImmutableArray<WarpPortableSourceNullableLayout> NullableLayouts()
        {
            var result = ImmutableArray.CreateBuilder<WarpPortableSourceNullableLayout>();
            foreach ((string identity, uint typeId) in typeIds.OrderBy(pair => pair.Value))
            {
                Type source = sources[identity];
                if (!WarpPortableMethodGraphIntrinsics.IsStructuralNullable(source)) { continue; }
                WarpPortableTypedType type = storage[identity];
                WarpPortableTypedField hasValue = type.Fields.First(field =>
                    string.Equals(fieldMetadata[field.Identity].SourceField.Name, "hasValue", StringComparison.Ordinal));
                WarpPortableTypedField value = type.Fields.First(field =>
                    string.Equals(fieldMetadata[field.Identity].SourceField.Name, "value", StringComparison.Ordinal));
                if (hasValue.ByteSize != 1 || hasValue.ByteOffset < 0 || value.ByteOffset < 0 ||
                    value.ByteSize != storage[value.TypeIdentity].ByteSize || hasValue.ByteOffset >= type.ByteSize ||
                    value.ByteSize > type.ByteSize - value.ByteOffset)
                {
                    throw new WarpVerificationException("WRPCLR2400", "A nullable source type has no exact captured HasValue/value byte layout.", 0);
                }
                result.Add(new(typeId, typeIds[value.TypeIdentity], (uint)hasValue.ByteOffset, (uint)value.ByteOffset));
            }
            return result.ToImmutable();
        }
    }
}
