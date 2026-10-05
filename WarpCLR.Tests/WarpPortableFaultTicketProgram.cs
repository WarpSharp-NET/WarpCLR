using System.Collections.Immutable;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal sealed record WarpPortableFaultTicketProgram(WarpPortableMethodGraph Graph, WarpPortableTypedProgram Typed,
    WarpPortableSourceHeapSchema Schema, WarpPortableWordBody Body, WarpPortableSourceFaultPoolPlan Pool,
    WarpPortableSourceFaultPoolRow Row, WarpLogicalMachineLayout Layout, WarpPortableExceptionPlan Exceptions,
    int Waiting, int SourceEntry);
