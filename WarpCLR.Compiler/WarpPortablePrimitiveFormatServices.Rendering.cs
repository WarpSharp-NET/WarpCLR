namespace WarpCLR.Compiler;

internal static partial class WarpPortablePrimitiveFormatServices
{
    private static uint Append(uint[] arena, uint scratch, uint character)
    {
        arena[scratch + WarpPortablePrimitiveFormatLayout.Render + arena[scratch]] = character;
        arena[scratch]++;
        return 0;
    }

    private static uint Symbol(uint[] arena, uint contract, uint scratch, uint symbol)
    {
        uint start = contract + arena[contract + 3 + symbol * 2];
        uint length = arena[contract + 4 + symbol * 2];
        for (uint word = 0; word < length; word++) { Append(arena, scratch, arena[start + word]); }
        return 0;
    }

    private static uint RenderInteger(uint[] arena, uint contract, uint scratch)
    {
        if (arena[scratch + 1] != 0) { Symbol(arena, contract, scratch, 0); }
        for (uint count = arena[scratch + 11]; count != 0; count--)
        { Append(arena, scratch, 48 + arena[scratch + WarpPortablePrimitiveFormatLayout.Digits + count - 1]); }
        return 0;
    }

    private static uint RenderFloat(uint[] arena, uint contract, uint scratch)
    {
        if (arena[scratch + 1] != 0) { Symbol(arena, contract, scratch, 0); }
        uint exponent = arena[scratch + 12];
        if ((exponent >> 31) == 0 ? exponent >= arena[scratch + 9] : unchecked(0U - exponent) > 4)
        { Scientific(arena, contract, scratch); }
        else { Fixed(arena, contract, scratch); }
        return 0;
    }

    private static uint Scientific(uint[] arena, uint contract, uint scratch)
    {
        uint count = arena[scratch + 11];
        uint digits = scratch + WarpPortablePrimitiveFormatLayout.Digits;
        Append(arena, scratch, 48 + arena[digits]);
        if (count > 1)
        {
            Symbol(arena, contract, scratch, 2);
            for (uint index = 1; index < count; index++) { Append(arena, scratch, 48 + arena[digits + index]); }
        }
        Append(arena, scratch, 69);
        uint exponent = arena[scratch + 12];
        if ((exponent >> 31) != 0) { Symbol(arena, contract, scratch, 0); exponent = unchecked(0U - exponent); }
        else { Symbol(arena, contract, scratch, 1); }
        if (exponent >= 100) { Append(arena, scratch, 48 + WarpPortableInteger32.DivideUnsigned(exponent, 100)); exponent = WarpPortableInteger32.RemainderUnsigned(exponent, 100); }
        Append(arena, scratch, 48 + WarpPortableInteger32.DivideUnsigned(exponent, 10));
        Append(arena, scratch, 48 + WarpPortableInteger32.RemainderUnsigned(exponent, 10));
        return 0;
    }

    private static uint Fixed(uint[] arena, uint contract, uint scratch)
    {
        uint exponent = arena[scratch + 12];
        uint count = arena[scratch + 11];
        uint digits = scratch + WarpPortablePrimitiveFormatLayout.Digits;
        if ((exponent >> 31) != 0)
        {
            Append(arena, scratch, 48);
            Symbol(arena, contract, scratch, 2);
            for (uint zero = 1; zero < unchecked(0U - exponent); zero++) { Append(arena, scratch, 48); }
            for (uint index = 0; index < count; index++) { Append(arena, scratch, 48 + arena[digits + index]); }
        }
        else
        {
            uint integerDigits = exponent + 1;
            for (uint index = 0; index < integerDigits; index++)
            { Append(arena, scratch, index < count ? 48 + arena[digits + index] : 48); }
            if (count > integerDigits)
            {
                Symbol(arena, contract, scratch, 2);
                for (uint index = integerDigits; index < count; index++) { Append(arena, scratch, 48 + arena[digits + index]); }
            }
        }
        return 0;
    }
}
