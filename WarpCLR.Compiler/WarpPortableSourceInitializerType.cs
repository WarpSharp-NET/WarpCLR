using System.Collections.Immutable;

namespace WarpCLR.Compiler;

internal sealed record WarpPortableSourceInitializerType(uint Type, string DeclaringType, string Initializer,
    bool BeforeFieldInit, uint WrapperType, uint DefaultHResult, ImmutableArray<ushort> ExceptionTypeName);

