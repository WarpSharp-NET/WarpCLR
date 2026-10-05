using System.Runtime.ExceptionServices;

namespace WarpCLR.Runtime.Host.Native;

internal sealed class WarpNativeCleanup
{
    private ExceptionDispatchInfo? failure;

    internal void Attempt(Action action)
    {
        try { action(); }
        catch (WarpHostException exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
    }

    internal void Complete() => failure?.Throw();
}
