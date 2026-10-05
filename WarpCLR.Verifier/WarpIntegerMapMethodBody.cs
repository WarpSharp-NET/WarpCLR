using System.Collections.Immutable;
using WarpCLR.IR;

namespace WarpCLR.Verifier;

internal sealed class WarpIntegerMapMethodBody
{
    public WarpIntegerMapMethodBody(
        string identity,
        int parameterCount,
        int inputBufferCount,
        int maxStack,
        ImmutableArray<WarpMetadataType> localTypes,
        ReadOnlySpan<byte> il,
        WarpReductionOperation? reduction = null,
        bool localsInitialized = false,
        IReadOnlyDictionary<int, WarpCilCallTarget>? callTargets = null,
        bool wordArena = false,
        ImmutableArray<bool> arenaParameters = default,
        ImmutableHashSet<int>? arenaElementTokens = null,
        ImmutableArray<bool> stateParameters = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        WarpCompilationAdmission.Require("<CIL-method>", WarpCompilationResourceKind.IdentityCharacters, identity.Length, WarpCompilationAdmission.MaximumIdentityCharacters);
        ArgumentOutOfRangeException.ThrowIfNegative(parameterCount);
        ArgumentOutOfRangeException.ThrowIfNegative(inputBufferCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(inputBufferCount, parameterCount);
        ArgumentOutOfRangeException.ThrowIfNegative(maxStack);
        if (localTypes.IsDefault)
        {
            throw new ArgumentException("Local storage types must be specified.", nameof(localTypes));
        }

        WarpCompilationAdmission.Require(identity, WarpCompilationResourceKind.Parameters, parameterCount, WarpCompilationAdmission.MaximumParametersPerBody);
        WarpCompilationAdmission.Require(identity, WarpCompilationResourceKind.Locals, localTypes.Length, WarpCompilationAdmission.MaximumLocalsPerBody);
        WarpCompilationAdmission.Require(identity, WarpCompilationResourceKind.EvaluationStack, maxStack, WarpCompilationAdmission.MaximumEvaluationStackPerBody);
        WarpCompilationAdmission.Require(identity, WarpCompilationResourceKind.CilBytes, il.Length, WarpCompilationAdmission.MaximumCilBytesPerBody);
        if (localTypes.Any(type => type is not WarpMetadataType.UInt32 and not WarpMetadataType.Boolean))
        {
            throw new WarpVerificationException("WRPCIL1000",
                $"Method '{identity}' is invalid. All local variables must have type System.UInt32 or System.Boolean.");
        }

        if (reduction.HasValue && !Enum.IsDefined(reduction.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(reduction));
        }

        Identity = identity;
        ParameterCount = parameterCount;
        InputBufferCount = inputBufferCount;
        MaxStack = maxStack;
        LocalTypes = localTypes;
        Il = il.ToArray();
        Reduction = reduction;
        LocalsInitialized = localsInitialized;
        CallTargets = callTargets ?? new Dictionary<int, WarpCilCallTarget>();
        WordArena = wordArena;
        if ((!arenaParameters.IsDefault && arenaParameters.Length != parameterCount) ||
            (!wordArena && !arenaParameters.IsDefault && arenaParameters.Any(value => value)))
        {
            throw new ArgumentException("Arena parameter capabilities must match the admitted signature.", nameof(arenaParameters));
        }

        ArenaParameters = arenaParameters.IsDefault ? ImmutableArray.CreateRange(Enumerable.Repeat(false, parameterCount)) : arenaParameters;
        if (!stateParameters.IsDefault && (stateParameters.Length != parameterCount ||
            stateParameters.Where((state, index) => state && !ArenaParameters[index]).Any()))
        {
            throw new ArgumentException("State parameter capabilities must bind an admitted array parameter.", nameof(stateParameters));
        }
        StateParameters = stateParameters.IsDefault ? ImmutableArray.CreateRange(Enumerable.Repeat(false, parameterCount)) : stateParameters;
        ArenaElementTokens = arenaElementTokens ?? ImmutableHashSet<int>.Empty;
    }

    public string Identity { get; }

    public int ParameterCount { get; }

    public int InputBufferCount { get; }

    public int MaxStack { get; }

    public int LocalCount => LocalTypes.Length;

    public ImmutableArray<WarpMetadataType> LocalTypes { get; }

    public byte[] Il { get; }

    public WarpReductionOperation? Reduction { get; }

    public bool LocalsInitialized { get; }

    public IReadOnlyDictionary<int, WarpCilCallTarget> CallTargets { get; }

    internal bool WordArena { get; }

    internal ImmutableArray<bool> ArenaParameters { get; }

    internal ImmutableArray<bool> StateParameters { get; }

    internal ImmutableHashSet<int> ArenaElementTokens { get; }
}
