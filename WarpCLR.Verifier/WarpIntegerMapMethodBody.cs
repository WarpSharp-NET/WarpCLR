using System.Reflection;
using System.Reflection.Emit;
using WarpCLR.IR;

namespace WarpCLR.Verifier;

internal sealed class WarpIntegerMapMethodBody
{
    public WarpIntegerMapMethodBody(
        string identity,
        int parameterCount,
        int inputBufferCount,
        int maxStack,
        int localCount,
        ReadOnlySpan<byte> il,
        WarpReductionOperation? reduction = null,
        bool localsInitialized = false,
        IReadOnlyDictionary<int, WarpCilCallTarget>? callTargets = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        WarpCompilationAdmission.Require("<CIL-method>", WarpCompilationResourceKind.IdentityCharacters, identity.Length, WarpCompilationAdmission.MaximumIdentityCharacters);
        ArgumentOutOfRangeException.ThrowIfNegative(parameterCount);
        ArgumentOutOfRangeException.ThrowIfNegative(inputBufferCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(inputBufferCount, parameterCount);
        ArgumentOutOfRangeException.ThrowIfNegative(maxStack);
        ArgumentOutOfRangeException.ThrowIfNegative(localCount);
        WarpCompilationAdmission.Require(identity, WarpCompilationResourceKind.Parameters, parameterCount, WarpCompilationAdmission.MaximumParametersPerBody);
        WarpCompilationAdmission.Require(identity, WarpCompilationResourceKind.Locals, localCount, WarpCompilationAdmission.MaximumLocalsPerBody);
        WarpCompilationAdmission.Require(identity, WarpCompilationResourceKind.EvaluationStack, maxStack, WarpCompilationAdmission.MaximumEvaluationStackPerBody);
        WarpCompilationAdmission.Require(identity, WarpCompilationResourceKind.CilBytes, il.Length, WarpCompilationAdmission.MaximumCilBytesPerBody);
        if (reduction.HasValue && !Enum.IsDefined(reduction.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(reduction));
        }

        Identity = identity;
        ParameterCount = parameterCount;
        InputBufferCount = inputBufferCount;
        MaxStack = maxStack;
        LocalCount = localCount;
        Il = il.ToArray();
        Reduction = reduction;
        LocalsInitialized = localsInitialized;
        CallTargets = callTargets ?? new Dictionary<int, WarpCilCallTarget>();
    }

    public string Identity { get; }

    public int ParameterCount { get; }

    public int InputBufferCount { get; }

    public int MaxStack { get; }

    public int LocalCount { get; }

    public byte[] Il { get; }

    public WarpReductionOperation? Reduction { get; }

    public bool LocalsInitialized { get; }

    public IReadOnlyDictionary<int, WarpCilCallTarget> CallTargets { get; }
}
