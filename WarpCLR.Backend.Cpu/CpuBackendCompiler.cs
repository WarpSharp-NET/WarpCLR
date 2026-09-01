using System.Text;
using WarpCLR.IR;

namespace WarpCLR.Backend.Cpu;

public sealed class CpuBackendCompiler : IWarpBackendCompiler
{
    public WarpBackendKind Backend => WarpBackendKind.CpuReference;

    public WarpBackendArtifact Compile(WarpLinearKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);

        var plan = new StringBuilder();
        plan.AppendLine("warp.cpu.linear/0.1");
        plan.AppendLine(WarpDeviceAbi.DevelopmentConformanceMarker);
        plan.Append("entry=").AppendLine(WarpDeviceAbi.IntegerMapEntryPoint);

        foreach (WarpIrInstruction instruction in kernel.Instructions)
        {
            plan.Append(instruction.Result)
                .Append('=')
                .Append(instruction.OpCode)
                .Append(',')
                .Append(instruction.Left)
                .Append(',')
                .Append(instruction.Right)
                .Append(',')
                .AppendLine(instruction.Immediate.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        plan.Append("result=").AppendLine(kernel.Result.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return new WarpBackendArtifact(
            Backend,
            WarpArtifactFormat.CpuExpressionPlan,
            WarpDeviceAbi.IntegerMapEntryPoint,
            Encoding.UTF8.GetBytes(plan.ToString()));
    }
}
