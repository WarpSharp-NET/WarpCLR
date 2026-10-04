namespace WarpCLR.IR;

internal static class WarpManagedMemoryOpCode
{
    internal const WarpIrOpCode LoadWord = (WarpIrOpCode)0x101;
    internal const WarpIrOpCode StoreWord = (WarpIrOpCode)0x102;
    internal const WarpIrOpCode WordCount = (WarpIrOpCode)0x103;
    internal const WarpIrOpCode WordAddress = (WarpIrOpCode)0x104;

    internal static bool RequiresArena(WarpIrOpCode opCode) => opCode is LoadWord or StoreWord or WordCount or WordAddress;

    internal static bool RequiresBounds(WarpIrOpCode opCode) => RequiresArena(opCode) && opCode != WordCount;
}
