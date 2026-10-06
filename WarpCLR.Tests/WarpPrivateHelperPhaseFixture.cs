using WarpCLR.Backend.CoreCLR;
using WarpCLR.IR;

namespace WarpCLR.Tests;

internal sealed class WarpPrivateHelperPhaseFixture
{
    internal const int MaximumDepth = 8;
    internal const string Service = "warp.proof.helper-phase/arena-publish-controller-exact-u32-pair/0.1";
    internal WarpPrivateHelperPhaseFixture(bool boundaries = true, int mutation = -1)
    {
        Kernel = Create(boundaries, mutation);
        Layout = new(Kernel);
        Compiled = CoreCLRResumableKernel.Compile(Layout);
    }

    internal WarpControlFlowKernel Kernel { get; }
    internal WarpLogicalMachineLayout Layout { get; }
    internal CoreCLRResumableKernel Compiled { get; }

    internal void Invoke(uint[] state, uint[] arena, uint controller, int quantum) =>
        Compiled.CompiledEntryPoint.Invoke(null, [new uint[][] { [0] }, new uint[] { controller }, 0,
            state, MaximumDepth, quantum, CancellationToken.None, arena]);

    internal uint[] ParkAfterGuestReturn(uint[] arena, int quantum)
    {
        uint[] state = Layout.CreateInitialState(MaximumDepth, 100);
        Layout.SetSourceBoundaryMode(state, true);
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (state[WarpLogicalMachineLayout.SourceBoundaryStateOffset] == WarpLogicalMachineLayout.NeedsPrivateHelper) { return state; }
            if (state[WarpLogicalMachineLayout.SourceBoundaryStateOffset] == WarpLogicalMachineLayout.BeforeSourceBoundary)
            { WarpLogicalMachineLayout.AcknowledgeSourceBoundary(state); }
            Invoke(state, arena, 0xDEADBEEF, quantum);
            Assert.AreEqual(WarpLogicalMachineLayout.Runnable, state[0]);
        }
        throw new InvalidOperationException("The exact controlled guest return did not park at its private bridge.");
    }

    private static WarpControlFlowKernel Create(bool boundaries, int mutation)
    {
        WarpBasicBlock[] entry = [new(0, [], [new(0, 0, [], 2)], new WarpTupleReturnTerminator([0, 1]))];
        var bridge = new List<WarpIrInstruction>
        {
            new(4, WarpPrivateControllerOpCode.LoadController),
            new(5, WarpManagedFrameOpCode.LoadPrivateWord, immediate: 0), new(6, WarpManagedFrameOpCode.LoadPrivateWord, immediate: 1),
            new(7, 2, [4, 5, 6], 2), new(9, WarpManagedFrameOpCode.StorePrivateWord, 7, immediate: 2),
            new(10, WarpManagedFrameOpCode.StorePrivateWord, 8, immediate: 3),
        };
        if (mutation == 1) { bridge.Insert(0, new(13, WarpIrOpCode.Constant)); }
        WarpControlFlowFunction[] functions =
        [
            new(0, "source-caller", 0, [new(0, [], [new(0, 1, [], 2),
                new(2, WarpManagedFrameOpCode.StorePrivateWord, 0, immediate: 0), new(3, WarpManagedFrameOpCode.StorePrivateWord, 1, immediate: 1)], new WarpBranchTerminator(new(1, []))),
                new(1, [], bridge, new WarpBranchTerminator(new(2, []))),
                new(2, [], [new(11, WarpManagedFrameOpCode.LoadPrivateWord, immediate: 2), new(12, WarpManagedFrameOpCode.LoadPrivateWord, immediate: 3)], new WarpTupleReturnTerminator([11, 12]))]),
            new(1, "guest-return-pair", 0, [new(0, [], [new(0, WarpIrOpCode.Constant, immediate: 0xFEEDC0DE),
                new(1, WarpIrOpCode.Constant, immediate: 0x80000000)], new WarpTupleReturnTerminator([0, 1]))]),
            new(2, Service, 3, [new(0, [], [new(0, WarpIrOpCode.LoadArgument), new(1, WarpIrOpCode.LoadArgument, immediate: 1),
                new(2, WarpIrOpCode.LoadArgument, immediate: 2), new(3, WarpIrOpCode.Constant),
                new(4, WarpManagedMemoryOpCode.StoreWord, 3, 0), new(5, WarpIrOpCode.ExclusiveOr, 1, 2)], new WarpTupleReturnTerminator([0, 5]))]),
        ];
        WarpLogicalBodyMetadata[] bodies = [new(0, true, [0], countsSourceDepth: false), new(4, false, [1, mutation == 0 ? 1 : 0, 1]),
            new(0, false, [1]), new(0, mutation != 2, [0], countsSourceDepth: mutation == 2)];
        var use = new WarpPrivateControllerUse(1, mutation == 3 ? 2 : 1, 4, 2, 0, 7, Service);
        var metadata = new WarpLogicalExecutionMetadata(bodies, frameOwners: true, runtimeStateAccess: true,
            privateControllerProjection: new([use], boundaries));
        return new("controlled-private-helper-phase", 1, 1, entry, null, functions, metadata);
    }
}
