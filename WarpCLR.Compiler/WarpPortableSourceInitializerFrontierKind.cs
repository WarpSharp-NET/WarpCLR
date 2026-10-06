namespace WarpCLR.Compiler;

internal enum WarpPortableSourceInitializerFrontierKind
{
    OriginalInstruction = 1,
    SourceCall,
    ConstructorCall,
    InitializerCall,
    FilterEntry,
    HandlerEntry,
    GuardedStateDispatch,
    GuardedManagedTermination,
    SourceReturn,
}
