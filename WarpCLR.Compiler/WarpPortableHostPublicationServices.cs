namespace WarpCLR.Compiler;

internal static partial class WarpPortableHostPublicationServices
{
    internal const string Semantics = "warp.runtime-host.precise-word-publication/0.1";

    public static uint StoreWord(uint[] arena, uint offset, uint value)
    {
        if (offset >= (uint)arena.Length)
        {
            return 1;
        }
        arena[offset] = value;
        return 0;
    }
}
