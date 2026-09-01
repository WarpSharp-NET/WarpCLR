namespace WarpCLR.IR;

public sealed class WarpIntegerMapKernel
{
    internal WarpIntegerMapKernel(
        string name,
        int inputBufferCount,
        int scalarArgumentCount,
        WarpExpression result,
        WarpReductionOperation? reduction = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(inputBufferCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(scalarArgumentCount);
        ArgumentNullException.ThrowIfNull(result);
        if (reduction.HasValue && !Enum.IsDefined(reduction.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(reduction));
        }

        Name = name;
        InputBufferCount = inputBufferCount;
        ScalarArgumentCount = scalarArgumentCount;
        Result = result;
        Reduction = reduction;
    }

    public string Name { get; }

    public int InputBufferCount { get; }

    public int ScalarArgumentCount { get; }

    public WarpExpression Result { get; }

    public WarpReductionOperation? Reduction { get; }
}
