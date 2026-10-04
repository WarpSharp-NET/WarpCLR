using System.Collections.Immutable;

namespace WarpCLR.Verifier;

internal sealed partial class WarpPortableTypedTypeCatalog
{
    public bool Assignable(WarpPortableTypedValue value, string destination)
    {
        WarpPortableTypedType target = Get(destination);
        if (value.Category != target.Category)
        {
            return false;
        }

        if (target.Category is WarpPortableStackCategory.I4 or WarpPortableStackCategory.I8)
        {
            return true;
        }

        if (target.Category == WarpPortableStackCategory.Reference)
        {
            return value.IsNull || Source(destination).IsAssignableFrom(Source(value.TypeIdentity));
        }

        return string.Equals(value.TypeIdentity, destination, StringComparison.Ordinal);
    }

    public WarpPortableTypedValue Merge(WarpPortableTypedValue first, WarpPortableTypedValue second, int offset)
    {
        if (first.Category != second.Category || first.IsUninitializedThis != second.IsUninitializedThis)
        {
            throw new WarpVerificationException("WRPCLR2201", "A control-flow merge has incompatible exact stack categories or constructor state.", offset);
        }

        string identity = first.TypeIdentity;
        if (!string.Equals(identity, second.TypeIdentity, StringComparison.Ordinal))
        {
            identity = MergeIdentity(first, second, offset);
        }

        if (first.Category == WarpPortableStackCategory.FunctionTarget && !string.Equals(first.MethodTarget, second.MethodTarget, StringComparison.Ordinal))
        {
            throw new WarpVerificationException("WRPCLR2201", "A delegate method target changes at a control-flow merge.", offset);
        }

        ImmutableArray<WarpPortableTypedProvenance> provenance = first.Provenance.Concat(second.Provenance).Distinct()
            .OrderBy(origin => origin.Kind).ThenBy(origin => origin.OwnerMethod, StringComparer.Ordinal)
            .ThenBy(origin => origin.OwnerIndex).ThenBy(origin => origin.OwnerType, StringComparer.Ordinal)
            .ThenBy(origin => origin.ByteOffset).ThenBy(origin => origin.ByteLength).ToImmutableArray();
        if (provenance.Length > 64)
        {
            throw new WarpVerificationException("WRPCLR2201", "A byref merge exceeds the bounded lifetime-provenance set.", offset);
        }

        return first with { TypeIdentity = identity, WordCount = Get(identity).WordCount, Provenance = provenance,
            IsReadOnly = first.IsReadOnly || second.IsReadOnly, IsNull = first.IsNull && second.IsNull,
            SourceStorageType = string.Equals(first.SourceStorageType, second.SourceStorageType, StringComparison.Ordinal) ? first.SourceStorageType : null };
    }

    public static bool Same(WarpPortableTypedValue first, WarpPortableTypedValue second) =>
        string.Equals(first.TypeIdentity, second.TypeIdentity, StringComparison.Ordinal) && first.Category == second.Category &&
        first.WordCount == second.WordCount && first.IsReadOnly == second.IsReadOnly &&
        first.IsUninitializedThis == second.IsUninitializedThis && first.IsNull == second.IsNull &&
        string.Equals(first.MethodTarget, second.MethodTarget, StringComparison.Ordinal) &&
        string.Equals(first.SourceStorageType, second.SourceStorageType, StringComparison.Ordinal) && first.Provenance.SequenceEqual(second.Provenance);

    private string MergeIdentity(WarpPortableTypedValue first, WarpPortableTypedValue second, int offset)
    {
        if (first.Category is WarpPortableStackCategory.I4 or WarpPortableStackCategory.I8)
        {
            return Get(first.Category == WarpPortableStackCategory.I4 ? typeof(int) : typeof(long)).Identity;
        }

        if (first.Category != WarpPortableStackCategory.Reference)
        {
            throw new WarpVerificationException("WRPCLR2201", "A value/byref/float merge changes its exact managed type or width.", offset);
        }

        if (first.IsNull) { return second.TypeIdentity; }
        if (second.IsNull) { return first.TypeIdentity; }
        Type candidate = Source(first.TypeIdentity);
        Type other = Source(second.TypeIdentity);
        if (candidate.IsAssignableFrom(other)) { return first.TypeIdentity; }
        if (other.IsAssignableFrom(candidate)) { return second.TypeIdentity; }
        while (candidate.BaseType is { } parent)
        {
            if (parent.IsAssignableFrom(other)) { return Get(parent).Identity; }
            candidate = parent;
        }

        return Get(typeof(object)).Identity;
    }
}
