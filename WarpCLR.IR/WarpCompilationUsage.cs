using System.Collections.ObjectModel;

namespace WarpCLR.IR;

internal sealed class WarpCompilationUsage
{
    private readonly string identity;
    private readonly int maximumExpansion;
    private long blocks;
    private long instructions;
    private long values;
    private long operands;
    private long functions;

    public WarpCompilationUsage(string identity, int maximumExpansion = 1)
    {
        this.identity = identity;
        this.maximumExpansion = maximumExpansion;
    }

    public void AddKernel(WarpControlFlowKernel kernel)
    {
        functions += kernel.Functions.Count;
        Require(WarpCompilationResourceKind.Functions, functions, WarpCompilationAdmission.MaximumFunctionsPerEntry);
        AddBody(kernel.Blocks, kernel.ValueCount);
        foreach (WarpControlFlowFunction function in kernel.Functions)
        {
            AddBody(function.Blocks, function.ValueCount);
        }
    }

    public void AddBody(ReadOnlyCollection<WarpBasicBlock> body, int valueCount)
    {
        blocks += body.Count;
        values += valueCount;
        Require(WarpCompilationResourceKind.Blocks, blocks, WarpCompilationAdmission.MaximumBlocksPerEntry);
        Require(WarpCompilationResourceKind.ValueSlots, values, WarpCompilationAdmission.MaximumValueSlotsPerEntry);
        foreach (WarpBasicBlock block in body)
        {
            instructions += block.Instructions.Count;
            Require(WarpCompilationResourceKind.Instructions, instructions, WarpCompilationAdmission.MaximumInstructionsPerEntry);
            foreach (WarpIrInstruction instruction in block.Instructions)
            {
                operands += instruction.Arguments.Count;
            }

            operands += block.Terminator switch
            {
                WarpBranchTerminator branch => branch.Target.Arguments.Count,
                WarpConditionalBranchTerminator branch => branch.WhenNonZero.Arguments.Count + (long)branch.WhenZero.Arguments.Count,
                WarpTupleReturnTerminator tuple => tuple.Values.Count,
                _ => 0,
            };
            Require(WarpCompilationResourceKind.OperandReferences, operands, WarpCompilationAdmission.MaximumOperandReferencesPerEntry);
        }
    }

    private void Require(WarpCompilationResourceKind resource, long requested, int limit)
        => WarpCompilationAdmission.Require(identity, resource, requested, limit * (long)maximumExpansion);
}
