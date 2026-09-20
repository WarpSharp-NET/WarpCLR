namespace WarpCLR.IR;

public interface IWarpBackendCompiler
{
    WarpBackendKind Backend { get; }

    WarpBackendContract Contract { get; }

    WarpBackendArtifact Compile(WarpLinearKernel kernel);
}
