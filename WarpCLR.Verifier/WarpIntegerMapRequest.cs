using System.Reflection;

namespace WarpCLR.Verifier;

internal sealed class WarpIntegerMapRequest
{
    public WarpIntegerMapRequest(MethodInfo method, int inputBufferCount, bool wordArena = false,
        WarpWordBankBindings? wordBanks = null)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentOutOfRangeException.ThrowIfLessThan(inputBufferCount, 1);
        if (wordBanks is not null && !wordArena)
        {
            throw new ArgumentException("Runtime word-bank bindings require the trusted word-service profile.", nameof(wordBanks));
        }

        Method = method;
        InputBufferCount = inputBufferCount;
        WordArena = wordArena;
        WordBanks = wordBanks;
    }

    public MethodInfo Method { get; }

    public int InputBufferCount { get; }

    internal bool WordArena { get; }

    internal WarpWordBankBindings? WordBanks { get; }
}
