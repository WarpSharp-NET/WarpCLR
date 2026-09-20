using WarpCLR.IR;

namespace WarpCLR.Compiler;

public sealed class WarpIntegerMapLowerer
{
    public WarpControlFlowKernel Lower(WarpIntegerMapKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        return kernel.ControlFlow;
    }
}
