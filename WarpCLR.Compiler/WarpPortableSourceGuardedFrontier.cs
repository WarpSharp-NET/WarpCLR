namespace WarpCLR.Compiler;

internal sealed record WarpPortableSourceGuardedFrontier(int DispatchFunction, int DispatchProgramCounter,
    int TargetFunction, int TargetBlock, int EndFunction, int EndProgramCounter,
    int AliasOwnerFunction, int AliasPrefixWords);
