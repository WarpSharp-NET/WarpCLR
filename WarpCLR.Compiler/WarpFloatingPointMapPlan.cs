using System.Collections.ObjectModel;
using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal sealed class WarpFloatingPointMapPlan
{
    public WarpFloatingPointMapPlan(Type sourceType, int inputValueCount, IEnumerable<WarpLogicalMachineLayout> results)
    {
        SourceType = sourceType;
        InputValueCount = inputValueCount;
        Results = Array.AsReadOnly(results.ToArray());
    }

    public Type SourceType { get; }

    public int InputValueCount { get; }

    public int StorageWordsPerValue => SourceType == typeof(double) ? 2 : 1;

    public ReadOnlyCollection<WarpLogicalMachineLayout> Results { get; }
}
