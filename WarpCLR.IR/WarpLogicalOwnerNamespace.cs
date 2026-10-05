namespace WarpCLR.IR;

internal static class WarpLogicalOwnerNamespace
{
    private static long lastOwner;

    internal static uint Next()
    {
        while (true)
        {
            long previous = Volatile.Read(ref lastOwner);
            if (previous >= uint.MaxValue)
            {
                throw new InvalidOperationException("The nonreusing portable owner namespace is exhausted.");
            }
            long next = previous + 1;
            if (Interlocked.CompareExchange(ref lastOwner, next, previous) == previous)
            {
                return checked((uint)next);
            }
        }
    }
}
