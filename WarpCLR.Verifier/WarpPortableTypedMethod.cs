using System.Collections.Immutable;

namespace WarpCLR.Verifier;

internal sealed record WarpPortableTypedMethod(
    string Identity, ImmutableArray<string> ArgumentTypes, ImmutableArray<string> LocalTypes,
    string ReturnType, ImmutableArray<WarpPortableTypedInstruction> Instructions,
    int MaximumStackWords, int PrivateStorageWords, string? Intrinsic, WarpPortableTypedReturnSummary ReturnSummary);
