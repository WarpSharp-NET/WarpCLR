namespace WarpCLR.Tests.Production;

internal static class WarpPortableFaultTicketSources
{
    public static uint Divide(uint left, uint right)
    {
        try { return left / right; }
        catch (DivideByZeroException) { return 91; }
    }

    public static int Overflow(int value)
    {
        try { return checked(value + 1); }
        catch (OverflowException) { return 92; }
    }

    public static float Round(float value, int digits)
    {
        try { return MathF.Round(value, digits, MidpointRounding.ToEven); }
        catch (ArgumentOutOfRangeException) { return 93; }
    }
}
