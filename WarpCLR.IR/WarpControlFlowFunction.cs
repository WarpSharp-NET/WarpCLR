using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace WarpCLR.IR;

public sealed class WarpControlFlowFunction
{
    public WarpControlFlowFunction(
        int id,
        string name,
        int parameterCount,
        IEnumerable<WarpBasicBlock> blocks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        WarpCompilationAdmission.Require("<IR-function>", WarpCompilationResourceKind.IdentityCharacters, name.Length, WarpCompilationAdmission.MaximumIdentityCharacters);
        ArgumentOutOfRangeException.ThrowIfNegative(parameterCount);
        ArgumentNullException.ThrowIfNull(blocks);

        WarpCompilationAdmission.Require(name, WarpCompilationResourceKind.Parameters, parameterCount, WarpCompilationAdmission.MaximumParametersPerBody);
        WarpBasicBlock[] blockArray = WarpCompilationAdmission.Materialize(blocks, name, WarpCompilationResourceKind.Blocks, WarpCompilationAdmission.MaximumBlocksPerEntry);
        WarpControlFlowKernel.ValidateBodyShape(blockArray, nameof(blocks));
        WarpCompilationAdmission.ValidateDefinitions(name, Array.AsReadOnly(blockArray));
        Dictionary<int, WarpIrValueType> valueTypes =
            WarpControlFlowKernel.CollectBodyDefinitions(blockArray);
        WarpControlFlowKernel.ValidateBodyValues(valueTypes);

        Id = id;
        Name = name;
        ParameterCount = parameterCount;
        Blocks = Array.AsReadOnly(blockArray);
        Instructions = Array.AsReadOnly(blockArray.SelectMany(block => block.Instructions).ToArray());
        ValueCount = valueTypes.Count;
        int resultWords = -1;
        foreach (WarpBasicBlock block in blockArray)
        {
            int words = block.Terminator switch
            {
                WarpReturnTerminator => 1,
                WarpTupleReturnTerminator tuple => tuple.Values.Count,
                _ => -1,
            };
            if (words < 0)
            {
                continue;
            }

            if (resultWords >= 0 && words != resultWords)
            {
                throw new ArgumentException("Every helper return must match its result signature.", nameof(blocks));
            }

            resultWords = words;
        }

        ResultWordCount = resultWords < 0 ? 1 : resultWords;
    }

    public int Id { get; }

    public string Name { get; }

    public int ParameterCount { get; }

    public ReadOnlyCollection<WarpBasicBlock> Blocks { get; }

    public ReadOnlyCollection<WarpIrInstruction> Instructions { get; }

    public int ValueCount { get; }

    internal int ResultWordCount { get; }
}
