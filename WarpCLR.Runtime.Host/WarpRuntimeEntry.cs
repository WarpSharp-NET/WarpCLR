using WarpCLR.Compiler;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.Runtime.Host;

public sealed class WarpRuntimeEntry
{
    internal WarpRuntimeEntry(WarpVerifiedEntry entry)
    {
        Identity = entry.Identity;
        GraphHash = entry.GraphHash;
        Kernel = new WarpIntegerMapLowerer().Lower(entry.Kernel);
        IrHash = WarpIrHash.Compute(Kernel);
        Layout = new WarpLogicalMachineLayout(Kernel);
    }

    public string Identity { get; }

    public string GraphHash { get; }

    public string IrHash { get; }

    public int InputBufferCount => Kernel.InputBufferCount;

    public int ScalarArgumentCount => Kernel.ScalarArgumentCount;

    public WarpReductionOperation? Reduction => Kernel.Reduction;

    internal WarpControlFlowKernel Kernel { get; }

    internal WarpLogicalMachineLayout Layout { get; }
}
