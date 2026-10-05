using System.Collections.Immutable;

namespace WarpCLR.Compiler;

internal sealed record WarpPortableSourceHeapServiceFault(string MethodIdentity, int SourceOffset, short SourceOpCode,
    int EffectIndex, string BindingIdentity, uint Descriptor, uint ExceptionType,
    ImmutableArray<int> FaultOperandWords);
