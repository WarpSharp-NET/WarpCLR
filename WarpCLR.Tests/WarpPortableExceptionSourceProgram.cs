using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal sealed record WarpPortableExceptionSourceProgram(WarpPortableMethodGraph Graph, WarpPortableTypedProgram Typed,
    WarpPortableSourceHeapSchema Schema, WarpPortableWordLoweredProgram Lowered, WarpPortableExceptionPlan Plan,
    WarpLogicalMachineLayout Layout);
