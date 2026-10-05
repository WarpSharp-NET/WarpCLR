using WarpCLR.IR;

namespace WarpCLR.Runtime.Host;

internal static class WarpCompiledSourceBoundary
{
    internal const int StateOffset = WarpLogicalMachineLayout.SourceBoundaryStateOffset;
    internal const uint BeforeSource = WarpLogicalMachineLayout.BeforeSourceBoundary;
    internal const uint Acknowledged = WarpLogicalMachineLayout.AcknowledgedSourceBoundary;

    internal static void Configure(WarpLogicalMachineLayout layout, uint[] state) => layout.SetSourceBoundaryMode(state, true);

    internal static void Acknowledge(uint[] state) => WarpLogicalMachineLayout.AcknowledgeSourceBoundary(state);
}
