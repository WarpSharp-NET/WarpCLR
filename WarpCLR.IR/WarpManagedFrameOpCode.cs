namespace WarpCLR.IR;

internal static class WarpManagedFrameOpCode
{
    internal const WarpIrOpCode LoadPrivateWord = (WarpIrOpCode)0x120;
    internal const WarpIrOpCode StorePrivateWord = (WarpIrOpCode)0x121;

    internal static bool IsPrivate(WarpIrOpCode opCode) => opCode is LoadPrivateWord or StorePrivateWord;
}
