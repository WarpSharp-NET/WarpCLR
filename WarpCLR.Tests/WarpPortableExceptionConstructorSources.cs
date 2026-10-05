using System.Runtime.CompilerServices;

namespace WarpCLR.Tests.Production;

// Captured newobj/storage/region metadata for generated runtime-protocol tests.
// These reference calls are CLR oracles only; they are never dispatch fallbacks.
internal static class WarpPortableExceptionConstructorSources
{
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static int FailedClass(Exception error, bool accept)
    {
        int marker = 0;
        try
        {
            try { _ = new Throwing(error); }
            finally { marker = 1; }
        }
        catch (InvalidOperationException) when (accept) { return marker + 1; }
        catch (InvalidOperationException) { return marker + 2; }
        return 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static int ContinueClass(Exception error)
    {
        _ = new Continuing(error);
        return 4;
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static int FailedValue(Exception error)
    {
        try { _ = new Pair(error); }
        catch (InvalidOperationException) { return 7; }
        return 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static int FailedTwoClass(Exception error)
    {
        try { _ = new Throwing(error); _ = new Throwing(error); }
        catch (InvalidOperationException) { return 11; }
        return 0;
    }

    internal sealed class Throwing
    {
        public Throwing(Exception error) { throw error; }
    }

    internal sealed class Continuing
    {
        public Continuing(Exception error)
        {
            try { WarpPortableExceptionFixtureSources.Callee(error); }
            catch (InvalidOperationException) { }
        }
    }

    internal readonly struct Pair
    {
        public Pair(Exception error) { First = error; Second = error; throw error; }
        public Exception First { get; }
        public Exception Second { get; }
    }
}
