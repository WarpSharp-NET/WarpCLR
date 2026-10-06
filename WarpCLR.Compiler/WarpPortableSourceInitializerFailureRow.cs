namespace WarpCLR.Compiler;

internal sealed record WarpPortableSourceInitializerFailureRow(uint Id, uint TypeRecord,
    uint CapturedMethodIndex, WarpPortableSourceOperationOrigin Origin);
