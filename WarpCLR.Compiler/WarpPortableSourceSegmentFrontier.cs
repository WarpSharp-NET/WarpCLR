namespace WarpCLR.Compiler;

internal sealed record WarpPortableSourceSegmentFrontier(int ProgramCounter, int Function, WarpPortableSourceSegmentEndKind Kind);
