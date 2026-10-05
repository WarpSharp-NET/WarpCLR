using System.Collections.Immutable;

namespace WarpCLR.Compiler;

internal sealed record WarpPortableExceptionFaultBinding(uint Id, string MethodIdentity, int SourceOffset,
    short SourceOpCode, int EffectIndex, uint ExceptionType, bool UncatchableRuntimeTermination,
    string? BindingIdentity, uint Descriptor, ImmutableArray<int> FaultOperandWords);
