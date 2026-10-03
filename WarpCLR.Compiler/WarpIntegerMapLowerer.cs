using WarpCLR.IR;

namespace WarpCLR.Compiler;

public sealed class WarpIntegerMapLowerer
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Preserve the maintained public lowerer instance API and the compiler service dependency contract.")]
    public WarpControlFlowKernel Lower(WarpIntegerMapKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        return kernel.ControlFlow;
    }
}
