using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

internal sealed record WarpPortableSourceHeapFault(string MethodIdentity, int SourceOffset, short SourceOpCode,
    int EffectIndex, WarpPortableTypedFaultKind Kind, uint ExceptionType, bool UncatchableRuntimeTermination);
