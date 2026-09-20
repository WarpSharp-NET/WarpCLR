using WarpCLR.IR;

namespace WarpCLR.Backend.CoreCLR;

public sealed class CoreCLRBackendCompiler : IWarpBackendCompiler
{
    private static readonly WarpBackendContract DeclaredContract = CreateContract();

    public WarpBackendKind Backend => WarpBackendKind.CoreCLR;

    public WarpBackendContract Contract => DeclaredContract;

    public WarpBackendArtifact Compile(WarpControlFlowKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);

        return new WarpBackendArtifact(
            Backend,
            WarpArtifactFormat.CoreCLRPlan,
            WarpDeviceAbi.GetEntryPoint(kernel),
            WarpCoreCLRPlanCodec.Serialize(kernel));
    }

    private static WarpBackendContract CreateContract() => new(
        WarpProfileCatalog.ProfileId,
        [
            WarpIrOpCode.LoadInput,
            WarpIrOpCode.LoadScalar,
            WarpIrOpCode.Constant,
            WarpIrOpCode.BitwiseNot,
            WarpIrOpCode.Add,
            WarpIrOpCode.Subtract,
            WarpIrOpCode.Multiply,
            WarpIrOpCode.BitwiseAnd,
            WarpIrOpCode.BitwiseOr,
            WarpIrOpCode.ExclusiveOr,
            WarpIrOpCode.ShiftLeft,
            WarpIrOpCode.ShiftRightLogical,
            WarpIrOpCode.Equal,
            WarpIrOpCode.NotEqual,
            WarpIrOpCode.LessThanUnsigned,
            WarpIrOpCode.LessThanOrEqualUnsigned,
            WarpIrOpCode.GreaterThanUnsigned,
            WarpIrOpCode.GreaterThanOrEqualUnsigned,
            WarpIrOpCode.Select,
        ],
        [
            WarpControlFlowOperation.BlockArguments,
            WarpControlFlowOperation.Branch,
            WarpControlFlowOperation.ConditionalBranch,
            WarpControlFlowOperation.Return,
        ],
        [
            WarpReductionOperation.WrappingSum,
            WarpReductionOperation.Minimum,
            WarpReductionOperation.Maximum,
        ]);
}
