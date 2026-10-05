namespace WarpCLR.Compiler;

internal sealed record WarpPortableSourceFaultPoolRow(WarpPortableSourceFaultFactoryRow Factory, uint Function,
    uint MessageResource, uint ParamNameResource);
