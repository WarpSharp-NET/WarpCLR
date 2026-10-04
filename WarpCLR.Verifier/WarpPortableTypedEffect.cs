namespace WarpCLR.Verifier;

// Order is explicit in each instruction; source faults precede memory mutation.
internal enum WarpPortableTypedEffect
{
    PrivateRead, PrivateWrite, NullCheck, BoundsCheck, TypeCheck, OverflowCheck,
    DivideByZeroCheck, TypeInitialize, ReadMemory, WriteMemory, Allocate,
    Call, Return, Control, Conversion, AtomicSequential, Acquire, Release, Safepoint,
}
