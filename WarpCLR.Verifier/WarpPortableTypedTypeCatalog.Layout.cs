using System.Collections.Immutable;

namespace WarpCLR.Verifier;

internal sealed partial class WarpPortableTypedTypeCatalog
{
    private WarpPortableTypedType BuildStorage(string identity, Type source)
    {
        if (source.IsEnum)
        {
            WarpPortableTypedType underlying = Get(source.GetEnumUnderlyingType());
            return underlying with { Identity = identity, ElementType = underlying.Identity };
        }

        if (source.IsByRef)
        {
            return new(identity, WarpPortableStackCategory.ManagedByref, 0, false, 24, 4, 6, 0,
                Get(source.GetElementType()!).Identity, [], [0]);
        }

        if (source == typeof(void))
        {
            return new(identity, WarpPortableStackCategory.Void, 0, false, 0, 1, 0, 0, null, [], []);
        }

        if (!source.IsValueType)
        {
            return new(identity, WarpPortableStackCategory.Reference, 0, false, 12, 4, 3, 0,
                source.IsArray ? Get(source.GetElementType()!).Identity : null, [], [0]);
        }

        if (source == typeof(RuntimeTypeHandle) || source == typeof(RuntimeFieldHandle) || source == typeof(RuntimeMethodHandle))
        {
            return new(identity, WarpPortableStackCategory.Handle, 32, false, 4, 4, 1, 0, null, [], []);
        }

        if (source.IsPrimitive)
        {
            int bytes = source == typeof(bool) || source == typeof(byte) || source == typeof(sbyte) ? 1 :
                source == typeof(char) || source == typeof(short) || source == typeof(ushort) ? 2 :
                source == typeof(long) || source == typeof(ulong) || source == typeof(double) ? 8 : 4;
            WarpPortableStackCategory category = source == typeof(float) ? WarpPortableStackCategory.Binary32 :
                source == typeof(double) ? WarpPortableStackCategory.Binary64 : bytes == 8 ? WarpPortableStackCategory.I8 : WarpPortableStackCategory.I4;
            bool signed = source == typeof(sbyte) || source == typeof(short) || source == typeof(int) || source == typeof(long);
            return new(identity, category, bytes * 8, signed, bytes, bytes, (bytes + 3) / 4, 0, null, [], []);
        }

        (int size, int alignment, ImmutableArray<WarpPortableTypedField> members, ImmutableArray<int> roots) = BuildLayout(identity, 0, []);
        return new(identity, WarpPortableStackCategory.Value, 0, false, size, alignment, (size + 3) / 4, size, null, members, roots);
    }

    private void BuildReferenceLayout(string identity)
    {
        WarpPortableTypedType owner = Get(identity);
        WarpPortableMethodGraphType metadata = captured[identity];
        int inheritedBytes = 0;
        ImmutableArray<WarpPortableTypedField> inherited = [];
        if (metadata.BaseType is { } baseType && captured.ContainsKey(baseType))
        {
            BuildReferenceLayout(baseType);
            inheritedBytes = Get(baseType).InstanceByteSize;
            inherited = Get(baseType).Fields.Where(field => !field.IsStatic).ToImmutableArray();
        }

        (int size, _, ImmutableArray<WarpPortableTypedField> members, _) = BuildLayout(identity, inheritedBytes, inherited);
        types[identity] = owner with { InstanceByteSize = size, Fields = members };
    }

    private (int Size, int Alignment, ImmutableArray<WarpPortableTypedField> Fields, ImmutableArray<int> Roots) BuildLayout(
        string identity, int inheritedBytes, ImmutableArray<WarpPortableTypedField> inherited)
    {
        WarpPortableMethodGraphType? metadata = captured.GetValueOrDefault(identity);
        var members = inherited.ToBuilder();
        int size = inheritedBytes;
        int staticSize = 0;
        int alignment = 1;
        foreach (string fieldIdentity in metadata?.Fields ?? [])
        {
            WarpPortableMethodGraphField field = fields[fieldIdentity];
            WarpPortableTypedType member = Get(field.FieldType);
            int pack = metadata!.PackingSize == 0 ? 8 : metadata.PackingSize;
            int fieldAlignment = member.ManagedRootByteOffsets.IsEmpty ? Math.Min(pack, member.Alignment) : Math.Max(4, member.Alignment);
            int offset = field.IsStatic ? Align(staticSize, fieldAlignment) : field.DeclaredOffset ?? Align(size, fieldAlignment);
            int end = checked(offset + member.ByteSize);
            if (field.IsStatic) { staticSize = end; }
            else { size = Math.Max(size, end); alignment = Math.Max(alignment, fieldAlignment); }
            members.Add(new(field.Identity, field.FieldType, offset, member.ByteSize, field.IsStatic, field.IsReadOnly));
        }

        size = Align(Math.Max(size, metadata?.DeclaredSize ?? 0), alignment);
        WarpCLR.IR.WarpCompilationAdmission.Require(identity, WarpCLR.IR.WarpCompilationResourceKind.ValueSlots,
            size, WarpCLR.IR.WarpCompilationAdmission.MaximumValueSlotsPerEntry);
        ImmutableArray<WarpPortableTypedField> result = members.ToImmutable();
        ValidateOpaqueOverlaps(result);
        ImmutableArray<int> roots = ValidateRoots(result);
        return (Math.Max(1, size), alignment, result, roots);
    }

    private ImmutableArray<int> ValidateRoots(ImmutableArray<WarpPortableTypedField> members)
    {
        var roots = new HashSet<int>();
        foreach (WarpPortableTypedField field in members.Where(field => !field.IsStatic))
        {
            foreach (int root in Get(field.TypeIdentity).ManagedRootByteOffsets)
            {
                int offset = checked(field.ByteOffset + root);
                if (offset % 4 != 0 || members.Any(other => !other.IsStatic && other != field &&
                    other.ByteOffset < offset + 12 && other.ByteOffset + other.ByteSize > offset))
                {
                    throw Error("An explicit/packed managed-reference layout is misaligned or overlaps another field.");
                }

                roots.Add(offset);
            }
        }

        return roots.Order().ToImmutableArray();
    }

    private static int Align(int value, int alignment) => checked((value + alignment - 1) / alignment * alignment);
}
