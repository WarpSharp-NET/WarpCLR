using System.Collections.Immutable;
using System.Reflection;

namespace WarpCLR.Verifier;

internal sealed record WarpPortableMethodGraphMethod(
    int Id,
    string Identity,
    MethodBase SourceMethod,
    string ReturnType,
    ImmutableArray<string> ParameterTypes,
    ImmutableArray<string> LocalTypes,
    ImmutableArray<byte> Cil,
    ImmutableArray<WarpPortableMethodGraphInstruction> Instructions,
    ImmutableArray<WarpPortableMethodGraphExceptionRegion> ExceptionRegions,
    ImmutableArray<string> Dependencies,
    int MaximumStack,
    bool InitializeLocals,
    string? Intrinsic);
