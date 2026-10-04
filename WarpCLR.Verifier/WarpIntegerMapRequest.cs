using System.Reflection;

namespace WarpCLR.Verifier;

internal sealed class WarpIntegerMapRequest
{
    public WarpIntegerMapRequest(MethodInfo method, int inputBufferCount, bool wordArena = false)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentOutOfRangeException.ThrowIfLessThan(inputBufferCount, 1);

        Method = method;
        InputBufferCount = inputBufferCount;
        WordArena = wordArena;
    }

    public MethodInfo Method { get; }

    public int InputBufferCount { get; }

    internal bool WordArena { get; }
}
