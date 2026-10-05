namespace WarpCLR.IR;

internal static class WarpManagedWideAtomicOpCode
{
    internal const string Semantics = "warp.managed-word.atomics64-single-event-aligned-sc-acquire-release-alignment-fault9/0.2";
    internal const string OpenClValidation = "warpclr.opencl-int64-atomic-extensions/0.1";
    internal const string OpenClBaseExtension = "cl_khr_int64_base_atomics";
    internal const string OpenClExtendedExtension = "cl_khr_int64_extended_atomics";
    internal const WarpIrOpCode LoadSequential = (WarpIrOpCode)0x140;
    internal const WarpIrOpCode StoreSequential = (WarpIrOpCode)0x141;
    internal const WarpIrOpCode CompareExchange = (WarpIrOpCode)0x142;
    internal const WarpIrOpCode Exchange = (WarpIrOpCode)0x143;
    internal const WarpIrOpCode Add = (WarpIrOpCode)0x144;
    internal const WarpIrOpCode LoadAcquire = (WarpIrOpCode)0x145;
    internal const WarpIrOpCode StoreRelease = (WarpIrOpCode)0x146;
    internal const WarpIrOpCode Increment = (WarpIrOpCode)0x147;
    internal const WarpIrOpCode Decrement = (WarpIrOpCode)0x148;
    internal const WarpIrOpCode And = (WarpIrOpCode)0x149;
    internal const WarpIrOpCode Or = (WarpIrOpCode)0x14A;

    internal static bool IsAtomic(WarpIrOpCode opCode) => opCode is LoadSequential or StoreSequential or
        CompareExchange or Exchange or Add or LoadAcquire or StoreRelease or Increment or Decrement or And or Or;

    // The address is Left. Pair operands and results are always low word, high word.
    // CAS operands are comparand-low/high, replacement-low/high; it returns the old pair.
    internal static int OperandWords(WarpIrOpCode opCode) => opCode switch
    {
        LoadSequential or LoadAcquire or Increment or Decrement => 0,
        StoreSequential or StoreRelease or Exchange or Add or And or Or => 2,
        CompareExchange => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(opCode)),
    };
}
