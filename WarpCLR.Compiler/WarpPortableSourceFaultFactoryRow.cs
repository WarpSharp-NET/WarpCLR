using WarpCLR.Verifier;

namespace WarpCLR.Compiler;

// Each row describes a complete finite data constructor. Dynamic formatting and
// unbound inner/ActualValue data require a different tested row, never defaults.
internal sealed record WarpPortableSourceFaultFactoryRow(uint Id, string MethodIdentity, int SourceOffset,
    short OpCode, int EffectIndex, WarpPortableTypedFaultKind Kind, uint ExceptionType,
    string OperationIdentity, uint FaultDescriptor, string ResourceKey, string? ParamName);
