using System.Reflection;

namespace WarpCLR.Verifier;

public sealed class WarpIntegerMapRequest
{
    public WarpIntegerMapRequest(MethodInfo method, int inputBufferCount)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentOutOfRangeException.ThrowIfLessThan(inputBufferCount, 1);

        Method = method;
        InputBufferCount = inputBufferCount;
    }

    public MethodInfo Method { get; }

    public int InputBufferCount { get; }
}
