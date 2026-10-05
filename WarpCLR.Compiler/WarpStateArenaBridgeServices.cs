namespace WarpCLR.Compiler;

internal static class WarpStateArenaBridgeServices
{
    public static uint CopyOwnedWords(uint[] state, uint[] arena, uint value)
    {
        uint bankSize = (uint)state.Length;
        uint source = ReadStateWord(state, 48);
        uint output = WriteArenaWord(arena, 0, source ^ value);
        state[48] = arena[1];
        arena[2] = bankSize;
        return output;
    }

    public static uint CopyOwnedAddress(uint[] state, uint[] arena, uint value)
    {
        arena[0] = state[48];
        state[48] += value;
        arena[0] ^= state[48];
        return arena[0];
    }

    public static uint ReadStateWord(uint[] state, uint offset) => state[offset];

    public static uint WriteArenaWord(uint[] arena, uint offset, uint value)
    {
        arena[offset] = value;
        return value;
    }
}
