namespace WarpCLR.Tests;

internal static class WarpTestCollectionAssertions
{
    public static T SingleItem<T>(this IEnumerable<T> source, Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(predicate);
        using IEnumerator<T> enumerator = source.Where(predicate).GetEnumerator();
        Assert.IsTrue(enumerator.MoveNext(), "The collection must contain one matching item.");
        T result = enumerator.Current;
        Assert.IsFalse(enumerator.MoveNext(), "The collection must not contain a second matching item.");
        return result;
    }

    internal static T RequireExactlyOne<T>(this IEnumerable<T> source) => RequireExactlyOne(source, static _ => true);

    internal static T RequireExactlyOne<T>(this IEnumerable<T> source, Func<T, bool> predicate)
    {
        T[] matches = source.Where(predicate).ToArray();
        if (matches.Length != 1) { throw new InvalidOperationException("The proof requires exactly one matching owned source item."); }
        return matches[0];
    }
}
