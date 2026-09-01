namespace WarpCLR.Runtime.Device;

public enum WarpScopedDestination
{
    ActivationLocal,
    SameActivationObject,
    ReturnValue,
    StaticField,
    OutputBuffer,
    HostBoundary,
    LongerLivedObject,
}

public readonly record struct WarpScopedReferenceFlow(
    string Source,
    WarpScopedDestination Destination);

public sealed class WarpScopedLifetimeVerifier
{
    public void Verify(IEnumerable<WarpScopedReferenceFlow> flows)
    {
        ArgumentNullException.ThrowIfNull(flows);
        foreach (WarpScopedReferenceFlow flow in flows)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(flow.Source);
            if (flow.Destination is WarpScopedDestination.ActivationLocal or
                WarpScopedDestination.SameActivationObject)
            {
                continue;
            }

            throw new WarpDeviceRuntimeException(
                "WRPDEV1005",
                $"Scoped reference '{flow.Source}' cannot enter {flow.Destination}.");
        }
    }
}
