using WarpCLR.IR;

namespace WarpCLR.Backend.Cpu;

public sealed class CpuBackendCompiler : IWarpBackendCompiler
{
    public WarpBackendKind Backend => WarpBackendKind.CpuReference;

    public WarpBackendArtifact Compile(WarpLinearKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);

        return new WarpBackendArtifact(
            Backend,
            WarpArtifactFormat.CpuExpressionPlan,
            WarpDeviceAbi.GetEntryPoint(kernel),
            WarpCpuPlanCodec.Serialize(kernel));
    }
}
