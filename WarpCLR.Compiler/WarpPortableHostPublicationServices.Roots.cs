namespace WarpCLR.Compiler;

internal static partial class WarpPortableHostPublicationServices
{
    public static uint ClearRange(uint[] arena, uint offset, uint count)
    {
        if (offset > (uint)arena.Length || count > (uint)arena.Length - offset)
        {
            return 1;
        }
        for (uint word = 0; word < count; word++)
        {
            arena[offset + word] = 0;
        }
        return 0;
    }

    public static uint StoreRoot(uint[] arena, uint offset, uint context, uint slot, uint generation)
    {
        if ((uint)arena.Length < 3 || offset > (uint)arena.Length - 3)
        {
            return 1;
        }
        arena[offset] = context;
        arena[offset + 1] = slot;
        arena[offset + 2] = generation;
        return 0;
    }
}
