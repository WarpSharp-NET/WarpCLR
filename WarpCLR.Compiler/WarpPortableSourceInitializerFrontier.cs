namespace WarpCLR.Compiler;

internal sealed record WarpPortableSourceInitializerFrontier(WarpPortableSourceInitializerFrontierKind Kind,
    int ProgramCounter, int Function, int? TargetProgramCounter, int? TargetFunction, int? Continuation,
    int? FirstGuestProgramCounter, string? TargetMethodIdentity, int? TargetSourceOffset, bool RequiresRuntimeReceipt);
