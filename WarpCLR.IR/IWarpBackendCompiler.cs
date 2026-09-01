namespace WarpCLR.IR;

public interface IWarpBackendCompiler
{
    WarpBackendKind Backend { get; }

    WarpBackendArtifact Compile(WarpLinearKernel kernel);
}
