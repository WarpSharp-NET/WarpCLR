using System.Collections.ObjectModel;

namespace WarpCLR.IR;

public enum WarpReductionOperation
{
    WrappingSum,
    Minimum,
    Maximum,
}

public readonly record struct WarpReductionDescriptor(
    WarpReductionOperation Operation,
    uint Identity);

public static class WarpReductionContract
{
    private static readonly ReadOnlyCollection<WarpReductionDescriptor> OperationDescriptors =
        Array.AsReadOnly<WarpReductionDescriptor>(
        [
            new(WarpReductionOperation.WrappingSum, 0u),
            new(WarpReductionOperation.Minimum, uint.MaxValue),
            new(WarpReductionOperation.Maximum, 0u),
        ]);

    public static IReadOnlyList<WarpReductionDescriptor> Operations => OperationDescriptors;

    public static uint Reduce(
        WarpReductionOperation operation,
        ReadOnlySpan<uint> values)
    {
        uint result = GetDescriptor(operation).Identity;
        foreach (uint value in values)
        {
            result = Apply(operation, result, value);
        }

        return result;
    }

    public static uint Apply(
        WarpReductionOperation operation,
        uint left,
        uint right) => operation switch
        {
            WarpReductionOperation.WrappingSum => unchecked(left + right),
            WarpReductionOperation.Minimum => Math.Min(left, right),
            WarpReductionOperation.Maximum => Math.Max(left, right),
            _ => throw new ArgumentOutOfRangeException(
                nameof(operation),
                operation,
                "The reduction operation is not registered."),
        };

    public static WarpReductionDescriptor GetDescriptor(
        WarpReductionOperation operation)
    {
        foreach (WarpReductionDescriptor descriptor in Operations)
        {
            if (descriptor.Operation == operation)
            {
                return descriptor;
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(operation),
            operation,
            "The reduction operation is not registered.");
    }
}
