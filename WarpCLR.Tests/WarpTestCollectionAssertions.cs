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
}
