using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;

namespace WarpCLR.Tests.Production;

internal static class WarpPortableInitializerProjectionExecution
{
    internal const int Depth = 64;

    internal static (uint Result, uint[] State) Run(WarpPortableInitializerProjectionFixtures.Fixture fixture, uint[] arguments, uint[]? arena = null,
        bool stopBeforeSource = false)
    {
        WarpLogicalMachineLayout layout = fixture.Projection.Layout;
        CoreCLRResumableKernel compiled = CoreCLRResumableKernel.Compile(layout);
        uint[] state = layout.CreateInitialState(Depth, 1000000);
        if (stopBeforeSource) { layout.SetSourceBoundaryMode(state, enabled: true); }
        uint[][] inputs = arguments.Length == 0 ? [[0]] : arguments.Select(word => new[] { word }).ToArray();
        int attempts = 0;
        while (state[WarpLogicalMachineLayout.StatusOffset] == WarpLogicalMachineLayout.Runnable && attempts++ < 100000)
        {
            if (arena is null) { compiled.ExecuteQuantum(inputs, [], 0, state, Depth, layout.MaximumBlockCost); }
            else { compiled.ExecuteManagedQuantum(inputs, [], 0, state, Depth, layout.MaximumBlockCost, arena); }
            if (stopBeforeSource && state[WarpLogicalMachineLayout.SourceBoundaryStateOffset] == WarpLogicalMachineLayout.BeforeSourceBoundary) { break; }
        }
        Assert.IsLessThan(100000, attempts);
        return (state[layout.GetResultWordOffset(0, Depth)], state);
    }
}
