using System.Runtime.InteropServices;

namespace WarpCLR.Tests.Production;

internal static partial class WarpPortableClosedFrameExceptionCases
{
    // Captured source CIL only; the tests compile and execute generated kernels.
    // They never invoke these source methods/constructors as a host fallback.
    internal struct Pair
    {
        public Exception First;
        public Exception Second;

        public Pair(Exception error, uint fail)
        {
            First = error; Second = error;
            if (fail != 0) { throw error; }
        }
    }

    internal struct HandledPair
    {
        public Exception First;
        public Exception Second;

        public HandledPair(Exception error)
        {
            First = error; CatchInside(error); Second = error;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MixedViews
    {
        public uint Unsigned;
        public int Signed;
    }

    internal static class Sources
    {
        public static Pair NormalPair(Exception error) => new(error, 0);

        public static uint FailedPair(Exception error)
        {
            try { _ = new Pair(error, 1); }
            catch (InvalidOperationException) { return 101; }
            return 202;
        }

        public static uint FinallyPair(Exception error)
        {
            uint progress = 0;
            try { _ = new Pair(error, 1); }
            catch (InvalidOperationException) { progress = 100; }
            finally { progress += 7; }
            return progress;
        }

        public static uint FilterPair(Exception error, uint decision)
        {
            try { _ = new Pair(error, 1); }
            catch (InvalidOperationException) when (Accept(error, decision)) { return 301; }
            catch (InvalidOperationException) { return 302; }
            return 303;
        }

        public static uint FilterCallsOrdinaryConstructorFrame(Exception error)
        {
            try { _ = new Pair(error, 1); }
            catch (InvalidOperationException) when (ConstructInOrdinaryFrame(error)) { return 401; }
            catch (InvalidOperationException) { return 402; }
            return 403;
        }

        public static HandledPair ConstructorHandlesItsCallee(Exception error) => new(error);
        public static uint ConstructorEscapes(Exception error) { _ = new Pair(error, 1); return 501; }

        public static uint ProjectionTypes(Exception error, uint value)
        {
            _ = error;
            MixedViews fields = default;
            fields.Signed = -3;
            SetUnsigned(ref fields.Unsigned, value);
            if (value == 0) { throw error; }
            return fields.Unsigned;
        }

        public static uint AliasCreatesValueInItsOwnTail(Exception error)
        {
            try { throw error; }
            catch (InvalidOperationException) when (new Pair(error, 0).First is not null) { return 601; }
        }

        public static uint AliasBorrowsStorage(Exception error)
        {
            uint flag = 1;
            try { throw error; }
            catch (InvalidOperationException) when (Borrow(ref flag)) { return 701; }
        }

        public static uint FaultingMath(Exception error)
        {
            try { return (uint)Math.Abs(int.MinValue); }
            catch (OverflowException) { _ = new Pair(error, 0); return 801; }
        }

        public static uint ThrowNull()
        {
            try { throw null!; }
            catch (NullReferenceException) { return 901; }
        }
    }

    private static void CatchInside(Exception error)
    {
        try { throw error; }
        catch (InvalidOperationException) { }
    }

    private static bool Accept(Exception error, uint decision) => error is not null && (decision & 1) != 0;
    private static bool ConstructInOrdinaryFrame(Exception error) => new Pair(error, 0).First is not null;
    private static bool Borrow(ref uint flag) => flag != 0;
    private static void SetUnsigned(ref uint value, uint replacement) => value = replacement;
}
