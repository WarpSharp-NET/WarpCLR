namespace WarpCLR.IR;

public sealed class WarpIntegerMapKernel
{
    internal WarpIntegerMapKernel(
        string name,
        int inputBufferCount,
        int scalarArgumentCount,
        WarpExpression result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(inputBufferCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(scalarArgumentCount);
        ArgumentNullException.ThrowIfNull(result);

        Name = name;
        InputBufferCount = inputBufferCount;
        ScalarArgumentCount = scalarArgumentCount;
        Result = result;
    }

    public string Name { get; }

    public int InputBufferCount { get; }

    public int ScalarArgumentCount { get; }

    public WarpExpression Result { get; }
}
