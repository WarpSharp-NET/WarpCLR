using WarpCLR.IR;

namespace WarpCLR.Verifier;

internal sealed class WarpCilCompilationAdmission
{
    private readonly string identity;
    private readonly WarpModuleCompilationAdmission? module;
    private readonly bool includeCil;
    private long functions;
    private long cilBytes;
    private long instructions;
    private long blocks;
    private long workspace;
    private long values;
    private long loweredInstructions;

    public WarpCilCompilationAdmission(string identity, WarpModuleCompilationAdmission? module = null, bool includeCil = true)
    {
        this.identity = identity;
        this.module = module;
        this.includeCil = includeCil;
    }

    public void AdmitMethod(string bodyIdentity, int parameterCount, int maxStack, int localCount, int ilBytes, bool isEntry)
    {
        Require(bodyIdentity, WarpCompilationResourceKind.Parameters, parameterCount, WarpCompilationAdmission.MaximumParametersPerBody);
        Require(bodyIdentity, WarpCompilationResourceKind.Locals, localCount, WarpCompilationAdmission.MaximumLocalsPerBody);
        Require(bodyIdentity, WarpCompilationResourceKind.EvaluationStack, maxStack, WarpCompilationAdmission.MaximumEvaluationStackPerBody);
        Require(bodyIdentity, WarpCompilationResourceKind.CilBytes, ilBytes, WarpCompilationAdmission.MaximumCilBytesPerBody);
        functions += isEntry ? 0 : 1;
        cilBytes += ilBytes;
        Require(identity, WarpCompilationResourceKind.Functions, functions, WarpCompilationAdmission.MaximumFunctionsPerEntry);
        Require(identity, WarpCompilationResourceKind.CilBytes, cilBytes, WarpCompilationAdmission.MaximumCilBytesPerEntry);
        if (includeCil)
        {
            module?.AdmitCilMethod(ilBytes, isEntry);
        }
    }

    public void AdmitDecodedInstructions(int count)
    {
        instructions += count;
        Require(identity, WarpCompilationResourceKind.Instructions, instructions, WarpCompilationAdmission.MaximumInstructionsPerEntry);
        if (includeCil)
        {
            module?.AdmitDecodedInstructions(count);
        }
    }

    public void AdmitBlocks(WarpIntegerMapMethodBody method, int count)
    {
        blocks += count + 1L;
        long bodyWorkspace = count * ((long)method.ParameterCount + method.LocalCount + method.MaxStack);
        workspace += bodyWorkspace;
        Require(identity, WarpCompilationResourceKind.Blocks, blocks, WarpCompilationAdmission.MaximumBlocksPerEntry);
        Require(identity, WarpCompilationResourceKind.VerifierWorkspaceSlots, workspace, WarpCompilationAdmission.MaximumVerifierWorkspaceSlotsPerEntry);
        module?.AdmitWorkspace(bodyWorkspace);
    }

    public void AdmitValue()
    {
        values++;
        Require(identity, WarpCompilationResourceKind.ValueSlots, values, WarpCompilationAdmission.MaximumValueSlotsPerEntry);
        module?.AdmitValue();
    }

    public void AdmitLoweredInstructions(int count)
    {
        loweredInstructions += count;
        Require(identity, WarpCompilationResourceKind.Instructions, loweredInstructions, WarpCompilationAdmission.MaximumInstructionsPerEntry);
        module?.AdmitLoweredInstructions(count);
    }

    private static void Require(string identity, WarpCompilationResourceKind resource, long requested, int limit)
        => WarpCompilationAdmission.Require(identity, resource, requested, limit);
}
