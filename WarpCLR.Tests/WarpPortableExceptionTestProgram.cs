using System.Collections.Immutable;
using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Tests.Production;

internal sealed record WarpPortableExceptionTestProgram(WarpPortableMethodGraph Graph, WarpPortableTypedProgram Typed,
    WarpPortableSourceHeapSchema Schema, WarpLogicalMachineLayout Layout, ImmutableArray<WarpPortableExceptionBodyBinding> Bindings,
    WarpPortableExceptionPlan Plan, ImmutableDictionary<(int Function, int Offset), int> Entries);
