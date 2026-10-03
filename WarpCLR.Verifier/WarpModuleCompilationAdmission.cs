using WarpCLR.IR;

namespace WarpCLR.Verifier;

internal sealed class WarpModuleCompilationAdmission
{
    private readonly WarpCompilationUsage lowered = new("<module>", WarpCompilationAdmission.MaximumModuleExpansionFactor);
    private long functions;
    private long cilBytes;
    private long instructions;
    private long workspace;
    private long values;
    private long loweredInstructions;

    public void AdmitCilMethod(int bodyBytes, bool isEntry)
    {
        functions += isEntry ? 0 : 1;
        cilBytes += bodyBytes;
        Require(WarpCompilationResourceKind.Functions, functions, WarpCompilationAdmission.MaximumFunctionsPerEntry);
        Require(WarpCompilationResourceKind.CilBytes, cilBytes, WarpCompilationAdmission.MaximumCilBytesPerEntry);
    }

    public void AdmitDecodedInstructions(int count)
    {
        instructions += count;
        Require(WarpCompilationResourceKind.Instructions, instructions, WarpCompilationAdmission.MaximumInstructionsPerEntry);
    }

    public void AdmitKernel(WarpControlFlowKernel kernel) => lowered.AddKernel(kernel);

    public void AdmitWorkspace(long count)
    {
        workspace += count;
        Require(WarpCompilationResourceKind.VerifierWorkspaceSlots, workspace, WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry);
    }

    public void AdmitValue()
    {
        values++;
        Require(WarpCompilationResourceKind.ValueSlots, values, WarpCompilationAdmission.MaximumValueSlotsPerEntry);
    }

    public void AdmitLoweredInstructions(int count)
    {
        loweredInstructions += count;
        Require(WarpCompilationResourceKind.Instructions, loweredInstructions, WarpCompilationAdmission.MaximumInstructionsPerEntry);
    }

    private static void Require(WarpCompilationResourceKind resource, long requested, int limit)
        => WarpCompilationAdmission.Require("<module>", resource, requested, limit * (long)WarpCompilationAdmission.MaximumModuleExpansionFactor);
}
