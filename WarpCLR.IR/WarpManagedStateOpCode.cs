namespace WarpCLR.IR;

internal static class WarpManagedStateOpCode
{
    internal const WarpIrOpCode WordCount = (WarpIrOpCode)0x130;
    internal const WarpIrOpCode LoadWord = (WarpIrOpCode)0x131;
    internal const WarpIrOpCode StoreWord = (WarpIrOpCode)0x132;
    internal const WarpIrOpCode WordAddress = (WarpIrOpCode)0x133;

    internal static bool IsState(WarpIrOpCode opCode) => opCode is WordCount or LoadWord or StoreWord or WordAddress;
    internal static bool RequiresBounds(WarpIrOpCode opCode) => opCode is LoadWord or StoreWord;
}
