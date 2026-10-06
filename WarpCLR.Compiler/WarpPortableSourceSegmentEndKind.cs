namespace WarpCLR.Compiler;

internal enum WarpPortableSourceSegmentEndKind
{
    OriginalInstructionBoundary,
    BeforeGuestCall,
    NonlocalDispatch,
    SourceReturn,
    ManagedExceptionTerminal,
}
