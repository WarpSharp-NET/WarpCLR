using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace WarpCLR.Tests.Production;

// Captured metadata fixtures. Runtime witnesses compile their captured CIL or
// independent protocol graphs; these methods are never invoked as a fallback.
internal static class WarpPortableExceptionFixtureSources
{
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Never-invoked source metadata fixture verifies ordered catch(Exception) clauses.")]
    public static int Catch(Exception error)
    {
        try { throw error; }
        catch (InvalidOperationException) { return 17; }
        catch (Exception) { return 29; }
    }

    public static int Escape(Exception error) { throw error; }

    public static Exception ReturnException(Exception error)
    {
        try { throw error; }
        catch (InvalidOperationException returned) { return returned; }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Never-invoked source metadata fixture verifies filter search before catch(Exception).")]
    public static int SearchBeforeUnwind(Exception error, bool accept)
    {
        try
        {
            try { throw error; }
            finally { accept = true; }
        }
        catch (Exception) when (accept) { return 31; }
        catch (Exception) { return 37; }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Never-invoked source metadata fixture verifies exact rethrow propagation to catch(Exception).")]
    public static int Rethrow(Exception error)
    {
        try
        {
            try { throw error; }
            catch (Exception) { throw; }
        }
        catch (Exception) { return 43; }
    }

    public static int Leave(Exception error)
    {
        try { return error is null ? 47 : 53; }
        finally { error = null!; }
    }

    public static int Finally(Exception error)
    {
        int result = 0;
        try
        {
            try { throw error; }
            finally { result = 71; }
        }
        catch (InvalidOperationException) { return result; }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Never-invoked source metadata fixture verifies replacement propagation to catch(Exception).")]
    [SuppressMessage("Usage", "CA2219:Do not raise exceptions in exception clauses", Justification = "The normative EH replacement case requires a throw inside finally; this fixture is never invoked.")]
    [SuppressMessage("Usage", "MA0072:Do not throw from a finally block", Justification = "Duplicate of the narrowly documented CA2219 fixture exception for required replacement semantics.")]
    public static int Replace(Exception first, Exception second)
    {
        try
        {
            try { throw first; }
            finally { throw second; }
        }
        catch (Exception) { return 59; }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Never-invoked source metadata fixture verifies original protected call-site propagation to catch(Exception).")]
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static int Caller(Exception error)
    {
        try { return Callee(error); }
        catch (Exception) { return 61; }
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static int Callee(Exception error) { throw error; }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Captured filter-escape metadata; the actual source CIL is lowered and never invoked by reflection.")]
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static int FilterEscape(Exception first, Exception second)
    {
        try { throw first; }
        catch (Exception) when (ThrowFilter(second)) { return 79; }
        catch (Exception) { return 83; }
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static bool ThrowFilter(Exception error) { throw error; }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static int RethrowCaller(Exception error)
    {
        try { return RethrowCallee(error); }
        catch (InvalidOperationException) { return 137; }
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static int RethrowCallee(Exception error)
    {
        try { return Callee(error); }
        catch (InvalidOperationException) { throw; }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Captured valid filter call enters another source frame with its own EH filter.")]
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static int CalledFilter(Exception first, Exception second, bool accept)
    {
        try { throw first; }
        catch (Exception) when (FilterWithEh(second, accept)) { return 97; }
        catch (Exception) { return 101; }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Captured filter-callee frame has distinct lexical EH and alias ownership.")]
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static bool FilterWithEh(Exception error, bool accept)
    {
        try { throw error; }
        catch (Exception) when (accept) { return true; }
        catch (Exception) { return false; }
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static int RecursiveTrace(Exception error, int depth)
    {
        try { return RecursiveThrow(error, depth); }
        catch (InvalidOperationException) { return 131; }
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static int RecursiveThrow(Exception error, int depth)
    {
        if (depth > 0) { return RecursiveThrow(error, depth - 1); }
        throw error;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Captured nested catch/rethrow identity witness.")]
    [SuppressMessage("Design", "CA2200:Rethrow to preserve stack details", Justification = "The inner explicit throw must replace trace data before the outer rethrow restores its original caught trace.")]
    [SuppressMessage("Usage", "MA0027:Prefer rethrow to throwing caught exception", Justification = "The deliberate inner explicit throw distinguishes new-throw and rethrow semantics.")]
    public static int RepeatedObjectRethrow(Exception error)
    {
        try
        {
            try { throw error; }
            catch (Exception)
            {
                try { throw error; }
                catch (InvalidOperationException) { }
                throw;
            }
        }
        catch (Exception) { return 89; }
    }
}
