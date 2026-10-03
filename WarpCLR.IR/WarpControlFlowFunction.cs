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
        ArgumentOutOfRangeException.ThrowIfNegative(parameterCount);
        ArgumentNullException.ThrowIfNull(blocks);

        WarpBasicBlock[] blockArray = blocks.ToArray();
        WarpControlFlowKernel.ValidateBodyShape(blockArray, nameof(blocks));
        Dictionary<int, WarpIrValueType> valueTypes =
            WarpControlFlowKernel.CollectBodyDefinitions(blockArray);
        WarpControlFlowKernel.ValidateBodyValues(valueTypes);

        Id = id;
        Name = name;
        ParameterCount = parameterCount;
        Blocks = Array.AsReadOnly(blockArray);
        Instructions = Array.AsReadOnly(blockArray.SelectMany(block => block.Instructions).ToArray());
        ValueCount = valueTypes.Count;
    }

    public int Id { get; }

    public string Name { get; }

    public int ParameterCount { get; }

    public ReadOnlyCollection<WarpBasicBlock> Blocks { get; }

    public ReadOnlyCollection<WarpIrInstruction> Instructions { get; }

    public int ValueCount { get; }
}
