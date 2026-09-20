namespace WarpCLR.IR;

public sealed class WarpIntegerMapKernel
{
    internal WarpIntegerMapKernel(
        WarpControlFlowKernel controlFlow)
    {
        ArgumentNullException.ThrowIfNull(controlFlow);
        ControlFlow = controlFlow;
    }

    public string Name => ControlFlow.Name;

    public int InputBufferCount => ControlFlow.InputBufferCount;

    public int ScalarArgumentCount => ControlFlow.ScalarArgumentCount;

    public WarpReductionOperation? Reduction => ControlFlow.Reduction;

    public WarpControlFlowKernel ControlFlow { get; }
}
