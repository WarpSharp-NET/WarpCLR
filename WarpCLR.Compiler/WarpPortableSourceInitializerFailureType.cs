using System.Collections.Immutable;

namespace WarpCLR.Compiler;

internal sealed record WarpPortableSourceInitializerFailureType(uint Id, uint Type, uint WrapperType, uint DefaultHResult,
    ImmutableArray<ushort> TypeName, ImmutableArray<ushort> Message, string DataHash);
