using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal static class WarpPortablePrimitiveFormatKernels
{
    internal static WarpLogicalMachineLayout Create()
    {
        WarpLogicalMachineLayout layout = WarpWordArenaServiceLowerer.Lower(
            typeof(WarpPortablePrimitiveFormatServices).GetMethod(nameof(WarpPortablePrimitiveFormatServices.Format))!);
        WarpControlFlowKernel body = layout.Kernel;
        var identified = new WarpControlFlowKernel(body.Name + "/" + WarpPortablePrimitiveFormatLayout.Semantics + "/" +
            WarpPortablePrimitiveFormatLayout.CultureSemantics, body.InputBufferCount, body.ScalarArgumentCount,
            body.Blocks, body.Reduction, body.Functions);
        return new WarpLogicalMachineLayout(identified);
    }
}
