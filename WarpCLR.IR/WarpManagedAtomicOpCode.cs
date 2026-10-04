namespace WarpCLR.IR;

internal static class WarpManagedAtomicOpCode
{
    internal const WarpIrOpCode LoadSequential = (WarpIrOpCode)0x110;
    internal const WarpIrOpCode StoreSequential = (WarpIrOpCode)0x111;
    internal const WarpIrOpCode CompareExchange = (WarpIrOpCode)0x112;
    internal const WarpIrOpCode Exchange = (WarpIrOpCode)0x113;
    internal const WarpIrOpCode Add = (WarpIrOpCode)0x114;
    internal const WarpIrOpCode LoadAcquire = (WarpIrOpCode)0x115;
    internal const WarpIrOpCode StoreRelease = (WarpIrOpCode)0x116;
    internal const WarpIrOpCode Fence = (WarpIrOpCode)0x117;

    internal static bool IsAtomic(WarpIrOpCode opCode) => opCode is LoadSequential or StoreSequential or
        CompareExchange or Exchange or Add or LoadAcquire or StoreRelease or Fence;
}
