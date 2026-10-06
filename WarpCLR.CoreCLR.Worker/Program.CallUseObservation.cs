using WarpCLR.Backend.CoreCLR;

namespace WarpCLR.CoreCLR.Worker;

internal static partial class Program
{
    // Keep the ThreadStatic scope entirely inside one synchronous ordinary quantum.
    // No private controller projection or owner-held bank is admitted by this path.
    private static byte[] ExecuteObserved(WarpCoreCLRWorkerProtocol.Frame frame, byte[] key, ulong sequence,
        CoreCLRResumableKernel kernel)
    {
        using CoreCLRCallUseObservation.Scope scope = CoreCLRCallUseObservation.Begin(frame, key, sequence, kernel);
        WarpCoreCLRWorkerWords.Invocation call = scope.Invocation;
        kernel.ExecuteManagedQuantum(call.Inputs, call.Scalars, call.Worker, call.State, call.Depth, call.Quantum,
            call.Arena, CancellationToken.None);
        return scope.Complete();
    }
}
