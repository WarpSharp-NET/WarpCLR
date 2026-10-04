namespace WarpCLR.Verifier;

internal enum WarpPortableTypedFaultKind
{
    NullReference, IndexOutOfRange, Overflow, DivideByZero, ArrayTypeMismatch,
    InvalidCast, TypeInitialization, AllocationQuota, CalledException,
}
