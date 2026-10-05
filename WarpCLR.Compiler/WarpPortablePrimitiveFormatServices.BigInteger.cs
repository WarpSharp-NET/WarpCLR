namespace WarpCLR.Compiler;

internal static partial class WarpPortablePrimitiveFormatServices
{
    private static uint Clear(uint[] arena, uint bank)
    {
        for (uint word = 0; word < WarpPortablePrimitiveFormatLayout.LimbWords; word++) { arena[bank + word] = 0; }
        return 0;
    }

    private static uint Set(uint[] arena, uint bank, uint low, uint high)
    {
        Clear(arena, bank);
        arena[bank] = low;
        arena[bank + 1] = high;
        return 0;
    }

    private static uint Copy(uint[] arena, uint destination, uint source)
    {
        for (uint word = 0; word < WarpPortablePrimitiveFormatLayout.LimbWords; word++)
        { arena[destination + word] = arena[source + word]; }
        return 0;
    }

    private static uint Compare(uint[] arena, uint left, uint right)
    {
        for (uint remaining = WarpPortablePrimitiveFormatLayout.LimbWords; remaining != 0; remaining--)
        {
            uint word = remaining - 1;
            if (arena[left + word] < arena[right + word]) { return 0; }
            if (arena[left + word] > arena[right + word]) { return 2; }
        }
        return 1;
    }

    private static uint Multiply(uint[] arena, uint bank, uint factor)
    {
        uint carry = 0;
        for (uint word = 0; word < WarpPortablePrimitiveFormatLayout.LimbWords; word++)
        {
            uint value = arena[bank + word];
            uint low = (value & 0xFFFF) * factor + carry;
            uint high = (value >> 16) * factor + (low >> 16);
            arena[bank + word] = (low & 0xFFFF) | (high << 16);
            carry = high >> 16;
        }
        return carry;
    }

    private static uint Shift(uint[] arena, uint bank, uint bits)
    {
        for (uint bit = 0; bit < bits; bit++)
        {
            if (Multiply(arena, bank, 2) != 0) { return WarpPortablePrimitiveFormatLayout.ArithmeticCapacity; }
        }
        return 0;
    }

    private static uint Add(uint[] arena, uint destination, uint left, uint right)
    {
        uint carry = 0;
        for (uint word = 0; word < WarpPortablePrimitiveFormatLayout.LimbWords; word++)
        {
            uint first = unchecked(arena[left + word] + arena[right + word]);
            uint next = first < arena[left + word] ? 1U : 0U;
            uint sum = unchecked(first + carry);
            next |= sum < first ? 1U : 0U;
            arena[destination + word] = sum;
            carry = next;
        }
        return carry;
    }

    private static uint Subtract(uint[] arena, uint left, uint right)
    {
        uint borrow = 0;
        for (uint word = 0; word < WarpPortablePrimitiveFormatLayout.LimbWords; word++)
        {
            uint first = unchecked(arena[left + word] - arena[right + word]);
            uint next = arena[left + word] < arena[right + word] ? 1U : 0U;
            uint difference = unchecked(first - borrow);
            next |= difference > first ? 1U : 0U;
            arena[left + word] = difference;
            borrow = next;
        }
        return 0;
    }

    private static uint Digit(uint[] arena, uint value, uint scale)
    {
        uint digit = 0;
        while (Compare(arena, value, scale) != 0)
        {
            Subtract(arena, value, scale);
            digit++;
        }
        return digit;
    }
}
