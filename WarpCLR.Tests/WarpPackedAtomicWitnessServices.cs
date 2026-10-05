namespace WarpCLR.Tests;

internal static class WarpPackedAtomicWitnessServices
{
    public static uint AppendOrdinal(uint[] arena, uint controllerWord, uint token, uint ordinalWord)
    {
        if (token == 0 || arena[controllerWord] != token) { return 0; }
        uint next = arena[ordinalWord] + 1;
        arena[ordinalWord] = next;
        return next;
    }

    public static uint WriteData(uint[] arena, uint controllerWord, uint token, uint dataWord, uint value)
    {
        if (token == 0 || arena[controllerWord] != token) { return 0; }
        arena[dataWord] = value;
        return 1;
    }

    public static uint ReadData(uint[] arena, uint controllerWord, uint token, uint dataWord)
    {
        if (token == 0 || arena[controllerWord] != token) { return 0; }
        return arena[dataWord];
    }
}
