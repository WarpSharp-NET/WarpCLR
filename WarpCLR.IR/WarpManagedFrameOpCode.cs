namespace WarpCLR.IR;

internal static class WarpManagedFrameOpCode
{
    internal const WarpIrOpCode LoadPrivateWord = (WarpIrOpCode)0x120;
    internal const WarpIrOpCode StorePrivateWord = (WarpIrOpCode)0x121;
    internal const WarpIrOpCode OwnerContext = (WarpIrOpCode)0x122;
    internal const WarpIrOpCode OwnerFrame = (WarpIrOpCode)0x123;
    internal const WarpIrOpCode OwnerGeneration = (WarpIrOpCode)0x124;

    internal static bool IsPrivate(WarpIrOpCode opCode) => opCode is LoadPrivateWord or StorePrivateWord;
    internal static bool IsOwner(WarpIrOpCode opCode) => opCode is OwnerContext or OwnerFrame or OwnerGeneration;
}
