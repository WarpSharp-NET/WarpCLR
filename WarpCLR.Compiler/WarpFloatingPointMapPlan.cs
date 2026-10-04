using WarpCLR.IR;

namespace WarpCLR.Compiler;

internal sealed class WarpFloatingPointMapPlan
{
    public WarpFloatingPointMapPlan(Type sourceType, int inputValueCount, WarpLogicalMachineLayout layout)
    {
        SourceType = sourceType;
        InputValueCount = inputValueCount;
        Layout = layout;
    }

    public Type SourceType { get; }

    public int InputValueCount { get; }

    public int StorageWordsPerValue => SourceType == typeof(double) ? 2 : 1;

    public WarpLogicalMachineLayout Layout { get; }
}
